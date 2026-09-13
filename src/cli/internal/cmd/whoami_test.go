package cmd_test

import (
	"bytes"
	"encoding/json"
	"net/http"
	"strings"
	"testing"

	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/exit"
)

func TestWhoamiSaysWhatAnAgentReaches(t *testing.T) {
	instance := serving(t, "9.9.9", answering(admitted))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "whoami")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	for _, line := range []string{"agent", "the deploy agent", "scratchpad", "read_write", "knowledge", "read", "none"} {
		if !strings.Contains(got.stdout, line) {
			t.Fatalf("whoami does not say %q: %q", line, got.stdout)
		}
	}

	// stdout is data and stderr is sentences: nothing explanatory here.
	if got.stderr != "" {
		t.Fatalf("whoami wrote to stderr: %q", got.stderr)
	}
}

func TestWhoamiAsJsonIsTheObjectTheApiAnswered(t *testing.T) {
	instance := serving(t, "9.9.9", answering(admitted))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "whoami", "--json")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	var body map[string]any
	if err := json.Unmarshal([]byte(got.stdout), &body); err != nil {
		t.Fatalf("not JSON: %v (%q)", err, got.stdout)
	}
	if body["kind"] != "agent" {
		t.Fatalf("the object is not the one the API answered: %v", body)
	}
}

func TestWhoamiWithARevokedTokenIsTheSameShutDoorAsWithNone(t *testing.T) {
	instance := serving(t, "9.9.9", refusing(
		http.StatusUnauthorized, `{"type":"/problems/unauthenticated","title":"No credential","status":401}`))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	if got := run(t, env, "whoami"); got.code != exit.Denied {
		t.Fatalf("want exit %d, got %d: %s", exit.Denied, got.code, got.stderr)
	}
}

func TestWhoamiWithNoCredentialAnywhereIsAUsageMistake(t *testing.T) {
	instance := serving(t, "9.9.9", answering(admitted))
	env := environment(t, map[string]string{config.EnvURL: instance.URL})

	got := runHolding(t, held{}, bytes.NewReader(nil), env, "whoami")

	if got.code != exit.Usage {
		t.Fatalf("want exit %d, got %d: %s", exit.Usage, got.code, got.stderr)
	}
	if len(instance.Requests) != 0 {
		t.Fatal("pea asked the instance without a credential to present")
	}
}
