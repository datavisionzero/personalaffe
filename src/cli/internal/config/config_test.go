package config_test

import (
	"errors"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/keychain"
)

func environment(values map[string]string) func(string) string {
	return func(name string) string { return values[name] }
}

func TestTheFlagBeatsTheEnvironmentWhichBeatsTheFile(t *testing.T) {
	in := config.Input{
		Getenv:  environment(map[string]string{config.EnvURL: "https://from-the-environment.example"}),
		File:    config.File{Instance: "https://from-the-file.example"},
		Address: "https://from-the-flag.example",
	}

	address, err := in.ResolveAddress()
	if err != nil {
		t.Fatal(err)
	}
	if address != "https://from-the-flag.example" {
		t.Fatalf("the flag did not win: %s", address)
	}

	in.Address = ""
	if address, _ = in.ResolveAddress(); address != "https://from-the-environment.example" {
		t.Fatalf("the environment did not win over the file: %s", address)
	}

	in.Getenv = environment(nil)
	if address, _ = in.ResolveAddress(); address != "https://from-the-file.example" {
		t.Fatalf("the file did not answer: %s", address)
	}
}

func TestNoInstanceAnywhereIsAUsageMistakeThatNamesTheVariable(t *testing.T) {
	_, err := config.Input{Getenv: environment(nil)}.ResolveAddress()

	var usage *config.UsageError
	if !errors.As(err, &usage) {
		t.Fatalf("want a usage error, got %v", err)
	}
	if !strings.Contains(usage.Message, config.EnvURL) {
		t.Fatalf("the message does not name %s: %s", config.EnvURL, usage.Message)
	}
}

func TestATrailingSlashIsNotPartOfTheAddress(t *testing.T) {
	in := config.Input{Getenv: environment(nil), Address: "https://workspace.example.com/"}

	if address, _ := in.ResolveAddress(); address != "https://workspace.example.com" {
		t.Fatalf("the slash survived: %s", address)
	}
}

func TestPlainHttpIsRefusedExceptToThisMachine(t *testing.T) {
	for _, address := range []string{"http://localhost:5000", "http://127.0.0.1:5000", "http://[::1]:5000"} {
		if err := config.CheckAddress(address, false); err != nil {
			t.Fatalf("%s is loopback and should be allowed: %v", address, err)
		}
	}

	err := config.CheckAddress("http://workspace.example.com", false)
	if err == nil {
		t.Fatal("plain HTTP to a public host should be refused")
	}
	if !strings.Contains(err.Error(), config.EnvInsecureHTTP) {
		t.Fatalf("the refusal does not say how to mean it: %v", err)
	}

	if err := config.CheckAddress("http://workspace.example.com", true); err != nil {
		t.Fatalf("saying you mean it should be enough: %v", err)
	}
}

func TestTheOverrideIsNeverInferred(t *testing.T) {
	in := config.Input{Getenv: environment(map[string]string{config.EnvInsecureHTTP: "maybe"})}
	if in.AllowsPlainHTTP() {
		t.Fatal("a word that is not yes should not turn the guard off")
	}

	in.Getenv = environment(map[string]string{config.EnvInsecureHTTP: "1"})
	if !in.AllowsPlainHTTP() {
		t.Fatal("1 should turn the guard off")
	}
}

func TestTheEnvironmentBeatsTheTokenFile(t *testing.T) {
	path := filepath.Join(t.TempDir(), "token")
	if err := os.WriteFile(path, []byte("from-the-file\n"), 0o600); err != nil {
		t.Fatal(err)
	}

	in := config.Input{
		Getenv: environment(map[string]string{config.EnvToken: "from-the-environment"}),
		File:   config.File{TokenFile: path},
	}

	token, from, err := in.ResolveToken("https://workspace.example.com")
	if err != nil {
		t.Fatal(err)
	}
	if token != "from-the-environment" || from != config.EnvToken {
		t.Fatalf("the environment did not win: %q from %q", token, from)
	}

	in.Getenv = environment(nil)
	token, from, err = in.ResolveToken("https://workspace.example.com")
	if err != nil {
		t.Fatal(err)
	}
	if token != "from-the-file" || from != path {
		t.Fatalf("the file did not answer: %q from %q", token, from)
	}
}

// held is a keychain in memory: the tests never touch the one on the machine
// running them.
type held map[string]string

func (h held) Load(instance string) (string, error) {
	if token, ok := h[instance]; ok {
		return token, nil
	}
	return "", keychain.ErrNotFound
}

func (h held) Save(instance, token string) error { h[instance] = token; return nil }

func (h held) Delete(instance string) error { delete(h, instance); return nil }

func (held) Where() string { return "a keychain in a test" }

func TestTheKeychainIsTheLastRungAndTheOthersComeFirst(t *testing.T) {
	const address = "https://workspace.example.com"

	path := filepath.Join(t.TempDir(), "token")
	if err := os.WriteFile(path, []byte("from-the-file\n"), 0o600); err != nil {
		t.Fatal(err)
	}

	in := config.Input{
		Getenv:   environment(map[string]string{config.EnvToken: "from-the-environment"}),
		File:     config.File{TokenFile: path},
		Keychain: held{address: "from-the-keychain"},
	}

	// The environment first, because that is how an agent receives its own and
	// how CI holds one.
	if token, from, _ := in.ResolveToken(address); token != "from-the-environment" || from != config.EnvToken {
		t.Fatalf("the environment did not win: %q from %q", token, from)
	}

	// Then the file somebody chose, because they chose it.
	in.Getenv = environment(nil)
	if token, from, _ := in.ResolveToken(address); token != "from-the-file" || from != path {
		t.Fatalf("the file did not answer: %q from %q", token, from)
	}

	// And only then the keychain, which nobody had to arrange.
	in.File = config.File{}
	token, from, err := in.ResolveToken(address)
	if err != nil {
		t.Fatal(err)
	}
	if token != "from-the-keychain" || from != config.Keychain {
		t.Fatalf("the keychain did not answer: %q from %q", token, from)
	}
}

func TestAnEmptyKeychainIsNoCredentialAndNotAFailure(t *testing.T) {
	in := config.Input{Getenv: environment(nil), Keychain: held{}}

	_, _, err := in.ResolveToken("https://workspace.example.com")

	if err == nil {
		t.Fatal("want a usage error")
	}
	if !strings.Contains(err.Error(), config.EnvToken) {
		t.Fatalf("the message does not name %s: %v", config.EnvToken, err)
	}
}

func TestAMachineWithNoKeychainStillResolvesTheOtherRungs(t *testing.T) {
	in := config.Input{
		Getenv:   environment(map[string]string{config.EnvToken: "from-the-environment"}),
		Keychain: nil,
	}

	if token, _, err := in.ResolveToken("https://workspace.example.com"); err != nil || token != "from-the-environment" {
		t.Fatalf("a machine with no keychain broke the ladder: %q, %v", token, err)
	}
}

func TestATokenFileAnybodyCanReadIsRefused(t *testing.T) {
	path := filepath.Join(t.TempDir(), "token")
	if err := os.WriteFile(path, []byte("a-token\n"), 0o644); err != nil {
		t.Fatal(err)
	}

	_, err := config.ReadTokenFile(path)
	if err == nil {
		t.Fatal("a world-readable token file should be refused")
	}
	if !strings.Contains(err.Error(), "chmod 600") {
		t.Fatalf("the refusal does not say how to fix it: %v", err)
	}
}

func TestNoTokenAnywhereNamesTheVariableAndNotAFlag(t *testing.T) {
	_, _, err := config.Input{Getenv: environment(nil)}.ResolveToken("https://workspace.example.com")

	if err == nil {
		t.Fatal("want a usage error")
	}
	if !strings.Contains(err.Error(), config.EnvToken) {
		t.Fatalf("the message does not name %s: %v", config.EnvToken, err)
	}
	// A credential is never a flag: it would stand in the shell history and in
	// `ps`. The message must not suggest one.
	if strings.Contains(err.Error(), "--token") {
		t.Fatalf("the message offers a flag for a credential: %v", err)
	}
}

func TestWhereTheConfigurationLives(t *testing.T) {
	path := func(values map[string]string) string {
		t.Helper()
		where, err := config.Path(environment(values))
		if err != nil {
			t.Fatal(err)
		}
		return where
	}

	if explicit := path(map[string]string{config.EnvConfig: "/somewhere/config.json"}); explicit != "/somewhere/config.json" {
		t.Fatalf("the explicit path did not win: %s", explicit)
	}

	if xdg := path(map[string]string{"XDG_CONFIG_HOME": "/x"}); xdg != filepath.Join("/x", "personalaffe", "config.json") {
		t.Fatalf("XDG_CONFIG_HOME was not used: %s", xdg)
	}

	home := path(map[string]string{"HOME": "/home/somebody"})
	if home != filepath.Join("/home/somebody", ".config", "personalaffe", "config.json") {
		t.Fatalf("the home directory was not used: %s", home)
	}
}

func TestAConfigurationFileThatIsNotThereIsAnEmptyOne(t *testing.T) {
	file, err := config.Load(filepath.Join(t.TempDir(), "nothing.json"))
	if err != nil {
		t.Fatalf("a missing file is not a broken install: %v", err)
	}
	if file.Instance != "" || file.TokenFile != "" {
		t.Fatalf("want an empty configuration, got %+v", file)
	}
}

func TestWhatIsSavedIsReadBackAndIsNobodyElseSBusiness(t *testing.T) {
	path := filepath.Join(t.TempDir(), "deeper", "config.json")
	written := config.File{Instance: "https://workspace.example.com"}

	if err := config.Save(path, written); err != nil {
		t.Fatal(err)
	}

	info, err := os.Stat(path)
	if err != nil {
		t.Fatal(err)
	}
	if mode := info.Mode().Perm(); mode&0o077 != 0 {
		t.Fatalf("the configuration is readable by others: %04o", mode)
	}

	read, err := config.Load(path)
	if err != nil {
		t.Fatal(err)
	}
	if read != written {
		t.Fatalf("what came back is not what went in: %+v", read)
	}
}
