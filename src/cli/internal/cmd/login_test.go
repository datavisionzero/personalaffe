package cmd_test

import (
	"bytes"
	"net/http"
	"strings"
	"testing"

	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/exit"
)

const admitted = `{"kind":"agent","email":null,"name":"the deploy agent",` +
	`"permissions":{"scratchpad":"read_write","knowledge":"read","tasks":"none","files":"none"},` +
	`"since":"2026-09-13T12:00:00.000000Z"}`

func TestLoginChecksTheTokenBeforeItKeepsIt(t *testing.T) {
	instance := serving(t, "9.9.9", answering(admitted))
	store := held{}
	env := environment(t, map[string]string{config.EnvURL: instance.URL})

	got := runHolding(t, store, strings.NewReader(secret+"\n"), env, "login", "--token-file", "-")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if store[instance.URL] != secret {
		t.Fatalf("the token is not in the keychain: %v", store)
	}

	// It was checked, and it was checked as the credential it is.
	if len(instance.Requests) != 1 || instance.Requests[0].URL.Path != "/api/me" {
		t.Fatalf("login did not ask who the token admits: %v", instance.Requests)
	}
	if presented := instance.Requests[0].Header.Get("Authorization"); presented != "Bearer "+secret {
		t.Fatalf("the token did not travel: %q", presented)
	}

	// Who it admits is a sentence for a person, on stderr, and never the token.
	if !strings.Contains(got.stderr, "the deploy agent") {
		t.Fatalf("login does not say who it admits: %q", got.stderr)
	}
	if strings.Contains(got.stdout+got.stderr, secret) {
		t.Fatal("the credential was printed")
	}
}

func TestLoginKeepsNothingWhenTheInstanceRefuses(t *testing.T) {
	instance := serving(t, "9.9.9", refusing(
		http.StatusUnauthorized, `{"type":"/problems/unauthenticated","title":"No credential","status":401}`))
	store := held{}
	env := environment(t, map[string]string{config.EnvURL: instance.URL})

	got := runHolding(t, store, strings.NewReader("pea_nope\n"), env, "login", "--token-file", "-")

	if got.code != exit.Denied {
		t.Fatalf("want exit %d, got %d: %s", exit.Denied, got.code, got.stderr)
	}
	if len(store) != 0 {
		t.Fatalf("a token that does not work was kept: %v", store)
	}
}

func TestLoginWritesTheInstanceDownButNeverTheToken(t *testing.T) {
	instance := serving(t, "9.9.9", answering(admitted))
	env := environment(t, map[string]string{config.EnvURL: instance.URL})

	if got := runHolding(
		t, held{}, strings.NewReader(secret), env, "login", "--token-file", "-"); got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	written, err := readFile(env(config.EnvConfig))
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(written, instance.URL) {
		t.Fatalf("the instance was not written down: %q", written)
	}
	if strings.Contains(written, secret) {
		t.Fatal("the configuration holds a credential")
	}
}

func TestLoginNeedsAFileAndNeverAnArgument(t *testing.T) {
	env := environment(t, map[string]string{config.EnvURL: "https://workspace.example.com"})

	got := runHolding(t, held{}, blocking{}, env, "login")

	if got.code != exit.Usage {
		t.Fatalf("want exit %d, got %d: %s", exit.Usage, got.code, got.stderr)
	}

	// And the message may not suggest one: a flag stands in the shell history,
	// in `ps`, and in whatever a CI runner logs.
	if strings.Contains(got.stderr, "--token ") {
		t.Fatalf("the message suggests a credential as an argument: %q", got.stderr)
	}
}

func TestLoginOnAMachineWithNoKeychainSaysSoAndSaysWhatToDo(t *testing.T) {
	instance := serving(t, "9.9.9", answering(admitted))
	env := environment(t, map[string]string{config.EnvURL: instance.URL})

	got := runHolding(t, held(nil), strings.NewReader(secret), env, "login", "--token-file", "-")

	if got.code != exit.Usage {
		t.Fatalf("want exit %d, got %d: %s", exit.Usage, got.code, got.stderr)
	}
	if !strings.Contains(got.stderr, config.EnvToken) {
		t.Fatalf("the refusal does not say what to do instead: %q", got.stderr)
	}
}

func TestLogoutIsLocalAndSaysSo(t *testing.T) {
	store := held{"https://workspace.example.com": secret}
	env := environment(t, map[string]string{config.EnvURL: "https://workspace.example.com"})

	got := runHolding(t, store, bytes.NewReader(nil), env, "logout")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if len(store) != 0 {
		t.Fatalf("the token is still in the keychain: %v", store)
	}

	// An agent that could revoke its own credential would be deciding
	// something about the instance.
	if !strings.Contains(got.stderr, "still works") {
		t.Fatalf("logout does not say the token still works: %q", got.stderr)
	}
}

func TestLogoutOnAnEmptyKeychainIsTheStateThatWasAskedFor(t *testing.T) {
	env := environment(t, map[string]string{config.EnvURL: "https://workspace.example.com"})

	if got := runHolding(t, held{}, bytes.NewReader(nil), env, "logout"); got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	// And so is a machine with no keychain at all.
	if got := runHolding(t, held(nil), bytes.NewReader(nil), env, "logout"); got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
}

func TestTheKeychainIsUsedWhenNothingAboveItAnswers(t *testing.T) {
	instance := serving(t, "9.9.9", answering(admitted))
	env := environment(t, map[string]string{config.EnvURL: instance.URL})

	got := runHolding(t, held{instance.URL: secret}, bytes.NewReader(nil), env, "whoami")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if presented := instance.Requests[0].Header.Get("Authorization"); presented != "Bearer "+secret {
		t.Fatalf("the keychain's token did not travel: %q", presented)
	}
}

func TestStatusSaysTheCredentialCameFromTheKeychainAndNotWhatItIs(t *testing.T) {
	instance := serving(t, "9.9.9", answering(`{"version":"9.9.9"}`))
	env := environment(t, map[string]string{config.EnvURL: instance.URL})

	got := runHolding(t, held{instance.URL: secret}, bytes.NewReader(nil), env, "status")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if !strings.Contains(got.stdout, config.Keychain) {
		t.Fatalf("status does not name the rung: %q", got.stdout)
	}
	if strings.Contains(got.stdout+got.stderr, secret) {
		t.Fatal("the credential was printed")
	}
}
