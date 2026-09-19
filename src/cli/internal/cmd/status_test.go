package cmd_test

import (
	"encoding/json"
	"net/http"
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

// byPath answers per address, which is what a command asking two anonymous
// operations in one run needs.
func byPath(answers map[string]string) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		body, found := answers[r.URL.Path]
		if !found {
			w.Header().Set("Content-Type", "application/problem+json")
			w.WriteHeader(http.StatusNotFound)
			_, _ = w.Write([]byte(`{"type":"/problems/not-found","title":"nothing there","status":404}`))
			return
		}

		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(body))
	}
}

func TestStatusNamesTheInstanceTheOwnerNamed(t *testing.T) {
	instance := serving(t, "9.9.9", byPath(map[string]string{
		"/api/version": `{"version":"9.9.9"}`,
		"/api/appearance": `{"title":"Haus","colour":"teal","shape":"circle",` +
			`"updated_at":"2026-09-19T13:44:33.325357Z"}`,
	}))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "status")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	// The name is what makes the address recognisable, and the address is what
	// makes the name unambiguous, so both are there.
	if !strings.Contains(got.stdout, "Haus") {
		t.Fatalf("status does not name the instance: %q", got.stdout)
	}
	if !strings.Contains(got.stdout, instance.URL) {
		t.Fatalf("status stopped saying which address: %q", got.stdout)
	}
}

func TestStatusSaysTheAddressAloneWhereNobodyNamedTheInstance(t *testing.T) {
	instance := serving(t, "9.9.9", byPath(map[string]string{
		"/api/version": `{"version":"9.9.9"}`,
		"/api/appearance": `{"title":null,"colour":"violet","shape":"square",` +
			`"updated_at":"2026-01-01T00:00:00.000000Z"}`,
	}))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "status")

	if !strings.Contains(got.stdout, "instance") || !strings.Contains(got.stdout, instance.URL) {
		t.Fatalf("status does not say which instance: %q", got.stdout)
	}
	if strings.Contains(got.stdout, "(") {
		t.Fatalf("an unnamed instance is the address and nothing else: %q", got.stdout)
	}
}

func TestStatusPrintsANameAsItWasWrittenAndNeverAsAnythingElse(t *testing.T) {
	instance := serving(t, "9.9.9", byPath(map[string]string{
		"/api/version": `{"version":"9.9.9"}`,
		"/api/appearance": `{"title":"<script>**x**</script>","colour":"violet","shape":"square",` +
			`"updated_at":"2026-01-01T00:00:00.000000Z"}`,
	}))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "status")

	if !strings.Contains(got.stdout, "<script>**x**</script>") {
		t.Fatalf("the name was not printed literally: %q", got.stdout)
	}
}

func TestStatusCarriesTheNameInJsonAndNullWhereThereIsNone(t *testing.T) {
	named := serving(t, "9.9.9", byPath(map[string]string{
		"/api/version": `{"version":"9.9.9"}`,
		"/api/appearance": `{"title":"Haus","colour":"violet","shape":"square",` +
			`"updated_at":"2026-01-01T00:00:00.000000Z"}`,
	}))

	var answered struct {
		Instance string  `json:"instance"`
		Name     *string `json:"name"`
	}

	got := run(t, environment(t, map[string]string{config.EnvURL: named.URL}), "status", "--json")
	if err := json.Unmarshal([]byte(got.stdout), &answered); err != nil {
		t.Fatalf("stdout is not JSON: %v\n%s", err, got.stdout)
	}
	if answered.Name == nil || *answered.Name != "Haus" {
		t.Fatalf("the name is missing: %+v", answered)
	}

	// And the address is still its own field: a client that read `instance`
	// yesterday reads the same thing today.
	if answered.Instance != named.URL {
		t.Fatalf("the address moved: %+v", answered)
	}

	unnamed := serving(t, "9.9.9", byPath(map[string]string{
		"/api/version": `{"version":"9.9.9"}`,
		"/api/appearance": `{"title":null,"colour":"violet","shape":"square",` +
			`"updated_at":"2026-01-01T00:00:00.000000Z"}`,
	}))

	answered.Name = nil
	got = run(t, environment(t, map[string]string{config.EnvURL: unnamed.URL}), "status", "--json")
	if err := json.Unmarshal([]byte(got.stdout), &answered); err != nil {
		t.Fatalf("stdout is not JSON: %v\n%s", err, got.stdout)
	}
	if answered.Name != nil {
		t.Fatalf("an unnamed instance answered a name: %+v", answered)
	}
}

func TestStatusStillWorksAgainstAnInstanceWithNoAppearanceEndpoint(t *testing.T) {
	// An older instance, or one that refused. A name is a convenience; a status
	// that failed over one would be useless exactly when it is wanted.
	instance := serving(t, "9.9.9", byPath(map[string]string{"/api/version": `{"version":"9.9.9"}`}))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "status")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if !strings.Contains(got.stdout, "9.9.9") {
		t.Fatalf("the instance's version is missing: %q", got.stdout)
	}
	if !strings.Contains(got.stdout, instance.URL) {
		t.Fatalf("status does not say which instance: %q", got.stdout)
	}
}
