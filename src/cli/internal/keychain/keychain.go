// Package keychain is the third rung of pea's credential ladder: where a
// machine keeps a token so that it is neither in an environment variable nor in
// a file somebody has to remember to chmod (docs/cli.md).
//
// It is the operating system's own store and not a file of ours. What that buys
// is the thing a file cannot: the secret is held by something with its own
// access control, screen lock and backup rules, and `pea` never has a copy of
// it on disk.
//
// Two stores are reached, both through the tool the system already ships, and
// **neither is ever given the secret as an argument**: an argument stands in
// `ps` for anybody on the machine to read, which is the whole thing this rung
// exists to avoid. macOS takes the command on standard input; `secret-tool`
// takes the secret on standard input. A machine with neither has no keychain,
// which is an answer rather than a failure — the two rungs above still work,
// and that is what CI and an agent's container use.
package keychain

import (
	"bytes"
	"errors"
	"fmt"
	"os/exec"
	"runtime"
	"strings"
)

// Service is what the entry is filed under, so that a person looking through
// their keychain finds a name they recognise.
const Service = "personalaffe"

// ErrNoKeychain is "this machine has nowhere to put it". Not a failure of the
// command that asked: the environment and a token file are still there.
var ErrNoKeychain = errors.New("no keychain on this machine")

// ErrNotFound is "nothing is stored for that instance".
var ErrNotFound = errors.New("no token in the keychain")

// Keychain is what pea asks; a test supplies its own.
type Keychain interface {
	// Load is the token stored for one instance, or ErrNotFound.
	Load(instance string) (string, error)
	// Save replaces whatever was there.
	Save(instance, token string) error
	// Delete removes it. Deleting nothing is not an error: the end state is
	// what was asked for.
	Delete(instance string) error
	// Where says which store this is, for `pea status` to print.
	Where() string
}

// OfThisMachine is the keychain this operating system has, or the one that has
// none.
func OfThisMachine() Keychain {
	switch runtime.GOOS {
	case "darwin":
		if path, err := exec.LookPath("security"); err == nil {
			return &macOS{tool: path}
		}
	case "linux":
		if path, err := exec.LookPath("secret-tool"); err == nil {
			return &libsecret{tool: path}
		}
	}

	return none{}
}

// none is a machine with no store, which every command has to cope with.
type none struct{}

func (none) Load(string) (string, error) { return "", ErrNoKeychain }
func (none) Save(string, string) error   { return ErrNoKeychain }
func (none) Delete(string) error         { return ErrNoKeychain }
func (none) Where() string               { return "" }

// macOS talks to the login keychain through `security`.
//
// Every call goes through its interactive mode, which reads the command from
// standard input. `security add-generic-password -w <token>` would work and
// would put the token in the argument list of a process, where `ps` shows it to
// whoever is on the machine — which is exactly the exposure this rung exists to
// avoid.
type macOS struct {
	tool string
	// run is the tool, or a test's stand-in. What it answers is what the tool
	// answers: its output, its complaint, and whether it exited non-zero.
	run func(command string) (out, complaint string, err error)
}

func (m *macOS) Where() string { return "the login keychain" }

func (m *macOS) Load(instance string) (string, error) {
	out, complaint, err := m.ask(fmt.Sprintf(
		"find-generic-password -s %s -a %s -w", quoted(Service), quoted(instance)))
	if err != nil {
		return "", classified(complaint, err)
	}

	token := strings.TrimSpace(out)
	if token == "" {
		return "", ErrNotFound
	}

	return token, nil
}

func (m *macOS) Save(instance, token string) error {
	// -U updates an entry that is already there rather than refusing.
	_, complaint, err := m.ask(fmt.Sprintf(
		"add-generic-password -U -s %s -a %s -l %s -w %s",
		quoted(Service), quoted(instance), quoted("personalaffe: "+instance), quoted(token)))
	if err != nil {
		return classified(complaint, err)
	}

	return nil
}

func (m *macOS) Delete(instance string) error {
	// A successful delete says "password has been deleted." on standard error
	// and exits 0. The exit code is the signal; what is written is a remark.
	_, complaint, err := m.ask(fmt.Sprintf(
		"delete-generic-password -s %s -a %s", quoted(Service), quoted(instance)))
	if err == nil {
		return nil
	}

	// Deleting nothing is the state that was asked for.
	failure := classified(complaint, err)

	if errors.Is(failure, ErrNotFound) {
		return nil
	}

	return failure
}

func (m *macOS) ask(command string) (string, string, error) {
	if m.run != nil {
		return m.run(command)
	}

	var out, complaint bytes.Buffer

	cmd := exec.Command(m.tool, "-i")
	cmd.Stdin = strings.NewReader(command + "\n")
	cmd.Stdout = &out
	cmd.Stderr = &complaint

	err := cmd.Run()

	return out.String(), complaint.String(), err
}

// libsecret talks to whatever answers the Secret Service API — GNOME Keyring,
// KWallet — through `secret-tool`, which reads the secret from standard input
// by design.
type libsecret struct{ tool string }

func (l *libsecret) Where() string { return "the system keyring" }

func (l *libsecret) Load(instance string) (string, error) {
	out, err := exec.Command(l.tool, "lookup", "service", Service, "account", instance).Output()
	if err != nil {
		return "", ErrNotFound
	}

	token := strings.TrimSpace(string(out))
	if token == "" {
		return "", ErrNotFound
	}

	return token, nil
}

func (l *libsecret) Save(instance, token string) error {
	cmd := exec.Command(
		l.tool, "store", "--label", "personalaffe: "+instance, "service", Service, "account", instance)
	cmd.Stdin = strings.NewReader(token)

	if out, err := cmd.CombinedOutput(); err != nil {
		return fmt.Errorf("%s: %s", err, strings.TrimSpace(string(out)))
	}

	return nil
}

func (l *libsecret) Delete(instance string) error {
	// Clearing nothing exits non-zero, and the end state is the one that was
	// asked for either way.
	_ = exec.Command(l.tool, "clear", "service", Service, "account", instance).Run()

	return nil
}

// classified turns "the tool exited non-zero" into the one answer a caller
// branches on: nothing is stored, or something else went wrong and is worth
// saying out loud — a locked keychain, a denied prompt.
func classified(complaint string, err error) error {
	if strings.Contains(complaint, "could not be found") ||
		strings.Contains(complaint, "SecKeychainSearchCopyNext") ||
		strings.Contains(complaint, "-25300") {
		return ErrNotFound
	}

	if trimmed := strings.TrimSpace(complaint); trimmed != "" {
		return fmt.Errorf("%w: %s", err, trimmed)
	}

	return err
}

// quoted is how a value travels inside a `security -i` command line. The
// values are an address and a generated token, neither of which contains a
// quote — and the escape is here so that neither has to be trusted not to.
func quoted(value string) string {
	return `"` + strings.NewReplacer(`\`, `\\`, `"`, `\"`).Replace(value) + `"`
}
