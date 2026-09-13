package cmd_test

import (
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/exit"
)

const secret = "a-token-that-must-never-be-printed"

func TestStatusSaysWhereEachAnswerCameFromAndNeverThePrint(t *testing.T) {
	instance := serving(t, "9.9.9", answering(`{"version":"9.9.9"}`))
	env := environment(t, map[string]string{
		config.EnvURL:   instance.URL,
		config.EnvToken: secret,
	})

	got := run(t, env, "status")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if !strings.Contains(got.stdout, instance.URL) {
		t.Fatalf("status does not say which instance: %q", got.stdout)
	}
	if !strings.Contains(got.stdout, config.EnvToken) {
		t.Fatalf("status does not say where the credential came from: %q", got.stdout)
	}

	// The whole point of printing the provenance is that the credential itself
	// never has to be printed to answer the question.
	if strings.Contains(got.stdout+got.stderr, secret) {
		t.Fatal("the credential was printed")
	}
}

func TestStatusInJsonCarriesTheProvenanceAndNotTheCredential(t *testing.T) {
	instance := serving(t, "9.9.9", answering(`{"version":"9.9.9"}`))
	env := environment(t, map[string]string{
		config.EnvURL:   instance.URL,
		config.EnvToken: secret,
	})

	got := run(t, env, "status", "--json")

	var answered struct {
		Instance string `json:"instance"`
		Token    struct {
			Configured bool   `json:"configured"`
			From       string `json:"from"`
		} `json:"token"`
	}
	if err := json.Unmarshal([]byte(got.stdout), &answered); err != nil {
		t.Fatalf("stdout is not JSON: %v\n%s", err, got.stdout)
	}

	if !answered.Token.Configured || answered.Token.From != config.EnvToken {
		t.Fatalf("the provenance is wrong: %+v", answered.Token)
	}
	if strings.Contains(got.stdout, secret) {
		t.Fatal("the credential was printed")
	}
}

func TestStatusSaysWhatIsMissingRatherThanFailingAtTheFirstThing(t *testing.T) {
	instance := serving(t, "9.9.9", answering(`{"version":"9.9.9"}`))
	env := environment(t, map[string]string{config.EnvURL: instance.URL})

	got := run(t, env, "status")

	// status is the command somebody runs *because* something is wrong. No
	// credential must not take the instance's version down with it.
	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if !strings.Contains(got.stdout, "9.9.9") {
		t.Fatalf("the instance's version is missing: %q", got.stdout)
	}
	if !strings.Contains(got.stdout, config.EnvToken) {
		t.Fatalf("status does not say what is missing: %q", got.stdout)
	}
}

func TestAnUnreadableTokenFileIsAUsageMistakeAndNotACredential(t *testing.T) {
	path := filepath.Join(t.TempDir(), "token")
	if err := os.WriteFile(path, []byte(secret+"\n"), 0o644); err != nil {
		t.Fatal(err)
	}

	configPath := filepath.Join(t.TempDir(), "config.json")
	if err := config.Save(configPath, config.File{
		Instance:  "https://workspace.example.com",
		TokenFile: path,
	}); err != nil {
		t.Fatal(err)
	}

	got := run(t, func(name string) string {
		if name == config.EnvConfig {
			return configPath
		}
		return ""
	}, "status")

	if !strings.Contains(got.stdout, "chmod 600") {
		t.Fatalf("status does not say how to fix the file: %q", got.stdout)
	}
	if strings.Contains(got.stdout+got.stderr, secret) {
		t.Fatal("the credential was printed")
	}
}
