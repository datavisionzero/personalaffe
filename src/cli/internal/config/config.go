// Package config is where pea learns which instance it talks to and as whom.
//
// Two questions, each answered through a ladder rather than by one variable:
// the instance is `--url`, then PERSONALAFFE_URL, then the instance written in
// the configuration file; the credential is PERSONALAFFE_TOKEN, then the token
// file if one was chosen. The environment wins for the credential because that
// is how an agent receives its own and how CI holds one.
//
// There is no project and no tenant: one instance belongs to one owner
// (CONTEXT.md). What is on disk is the instance and the path of a token file
// where one was named, and it holds no credential of its own.
//
// The third rung of the credential ladder — the operating system's keychain,
// where a browser sign-in would leave a session — is PERSONAL-E2's, along with
// the sign-in itself. The ladder is built with two rungs so that adding the
// third is an insertion rather than a redesign.
package config

import (
	"encoding/json"
	"errors"
	"fmt"
	"net"
	"net/url"
	"os"
	"path/filepath"
	"strings"
)

// Environment variables, in one place because they are a contract with CI, with
// containers and with whatever harness starts an agent.
const (
	EnvURL          = "PERSONALAFFE_URL"
	EnvToken        = "PERSONALAFFE_TOKEN"
	EnvInsecureHTTP = "PERSONALAFFE_INSECURE_HTTP"
	EnvConfig       = "PERSONALAFFE_CONFIG"
)

// UsageError is a mistake in the environment or the arguments: exit 2.
//
// Cause is what a command can ask about when one of these is not fatal to what
// it was doing — there is exactly one such case, and it is named below.
type UsageError struct {
	Message string
	Cause   error
}

func (e *UsageError) Error() string { return e.Message }

func (e *UsageError) Unwrap() error { return e.Cause }

// ErrNoInstance is "nobody has said which instance this is", which is the one
// usage mistake a command may decide to live with: `pea version` still knows
// what pea is. Every other mistake in an address — a scheme that is not one,
// plain HTTP to a public host — is a mistake about the instance that was named,
// and no command gets to carry on past it.
var ErrNoInstance = errors.New("no instance")

// File is the configuration as it is on disk: which instance, and where the
// credential went when it was put in a file. It holds no credential — a token
// file, where one was chosen, is its own file with its own permissions.
type File struct {
	Instance  string `json:"instance,omitempty"`
	TokenFile string `json:"token_file,omitempty"`
}

// Path is where the configuration lives: $PERSONALAFFE_CONFIG if it is set,
// otherwise $XDG_CONFIG_HOME/personalaffe/config.json, otherwise
// ~/.config/personalaffe/config.json.
func Path(getenv func(string) string) (string, error) {
	if getenv == nil {
		getenv = os.Getenv
	}
	if explicit := strings.TrimSpace(getenv(EnvConfig)); explicit != "" {
		return explicit, nil
	}
	if xdg := strings.TrimSpace(getenv("XDG_CONFIG_HOME")); xdg != "" {
		return filepath.Join(xdg, "personalaffe", "config.json"), nil
	}

	home := strings.TrimSpace(getenv("HOME"))
	if home == "" {
		var err error
		if home, err = os.UserHomeDir(); err != nil {
			return "", &UsageError{Message: fmt.Sprintf(
				"no home directory: set %s to say where the configuration lives.", EnvConfig)}
		}
	}

	return filepath.Join(home, ".config", "personalaffe", "config.json"), nil
}

// Load reads the configuration. A file that is not there is an empty one: a
// machine that has never been pointed at an instance is not a machine with a
// broken install.
func Load(path string) (File, error) {
	content, err := os.ReadFile(path)
	if errors.Is(err, os.ErrNotExist) {
		return File{}, nil
	}
	if err != nil {
		return File{}, &UsageError{Message: fmt.Sprintf("%s could not be read: %v", path, err)}
	}

	var file File
	if err := json.Unmarshal(content, &file); err != nil {
		return File{}, &UsageError{Message: fmt.Sprintf("%s is not readable as configuration: %v", path, err)}
	}

	return file, nil
}

// Save writes it, readable by its owner and nobody else. The directory is made
// with the same intent: which instance somebody works against is nobody else's
// business on a shared machine.
func Save(path string, file File) error {
	if err := os.MkdirAll(filepath.Dir(path), 0o700); err != nil {
		return err
	}

	content, err := json.MarshalIndent(file, "", "  ")
	if err != nil {
		return err
	}

	return os.WriteFile(path, append(content, '\n'), 0o600)
}

// ReadTokenFile reads a credential out of the file somebody chose, and refuses
// one anybody else on the machine can read. A file mode is the only protection
// a token in a file has, and shrugging at 0644 would be a quiet fallback to
// plaintext nobody agreed to.
func ReadTokenFile(path string) (string, error) {
	info, err := os.Stat(path)
	if err != nil {
		return "", &UsageError{Message: fmt.Sprintf(
			"%s holds this machine's token and could not be read: %v", path, err)}
	}
	if mode := info.Mode().Perm(); mode&0o077 != 0 {
		return "", &UsageError{Message: fmt.Sprintf(
			"%s is readable by others (mode %04o). A token in a file is protected by nothing else: `chmod 600 %s`.",
			path, mode, path)}
	}

	content, err := os.ReadFile(path)
	if err != nil {
		return "", &UsageError{Message: fmt.Sprintf("%s could not be read: %v", path, err)}
	}

	token := strings.TrimSpace(string(content))
	if token == "" {
		return "", &UsageError{Message: fmt.Sprintf("%s is empty.", path)}
	}

	return token, nil
}

// CheckAddress refuses plain HTTP to anything but a loopback host. A token over
// plain HTTP is a token in somebody's network log, and localhost is the one
// place that cannot be true. The override is explicit and is never inferred.
func CheckAddress(address string, allowPlainHTTP bool) error {
	parsed, err := url.Parse(address)
	if err != nil || !parsed.IsAbs() || parsed.Host == "" {
		return &UsageError{Message: fmt.Sprintf(
			"%q is not an address: scheme and host, like https://workspace.example.com.", address)}
	}

	switch parsed.Scheme {
	case "https":
		return nil
	case "http":
		if allowPlainHTTP || IsLoopback(parsed.Hostname()) {
			return nil
		}
		return &UsageError{Message: fmt.Sprintf(
			"%s is plain HTTP to a host that is not loopback, and a token over plain HTTP is a token in the network log. "+
				"Use https://, or pass --insecure-http (or %s=1) to say you mean it.", address, EnvInsecureHTTP)}
	default:
		return &UsageError{Message: fmt.Sprintf("%q is neither http nor https.", address)}
	}
}

// IsLoopback is the whole of what "this machine" means here: the two names and
// the two address families, and nothing that merely resolves to one.
func IsLoopback(host string) bool {
	if host == "localhost" || strings.HasSuffix(host, ".localhost") {
		return true
	}
	if ip := net.ParseIP(strings.Trim(host, "[]")); ip != nil {
		return ip.IsLoopback()
	}

	return false
}
