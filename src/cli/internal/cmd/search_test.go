package cmd_test

import (
	"encoding/json"
	"net/http"
	"strings"
	"testing"

	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/exit"
)

const theFindings = `{"query":"arch dec","has_more":true,"items":[
  {"application":"knowledge","id":"0199f0c4-0000-7000-8000-000000000001",
   "title":"Architecture decisions","snippet":"Where the storage decision\nlives and",
   "within":null,"updated_at":"2026-09-14T08:30:00.000000Z","rank":0.61},
  {"application":"files","id":"0199f0c4-0000-7000-8000-000000000002",
   "title":"architecture.pdf","snippet":null,
   "within":null,"updated_at":"2026-09-13T08:30:00.000000Z","rank":0.24}]}`

func TestSearchPrintsALinePerFinding(t *testing.T) {
	instance := serving(t, "9.9.9", answering(theFindings))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "search", "arch", "dec")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	lines := strings.Split(strings.TrimSpace(got.stdout), "\n")
	if len(lines) != 2 {
		t.Fatalf("want a line per finding, got %q", got.stdout)
	}

	// A snippet is a person's own text and can carry a newline; a column that
	// carried one would be a row `cut` cannot read.
	want := "knowledge\t0199f0c4-0000-7000-8000-000000000001\tArchitecture decisions\t" +
		"Where the storage decision lives and"
	if lines[0] != want {
		t.Fatalf("the first line is %q", lines[0])
	}

	// No snippet where there was no body to quote — a file name. The column is
	// still there and empty, so the shape of every line is the same.
	if !strings.HasSuffix(got.stdout, "architecture.pdf\t\n") {
		t.Fatalf("a finding with no snippet is not printed plainly: %q", got.stdout)
	}

	if !strings.Contains(got.stderr, "--limit") {
		t.Fatalf("a cut-off answer does not say so: %q", got.stderr)
	}
}

func TestSearchJoinsItsArgumentsSoNobodyHasToQuoteASearch(t *testing.T) {
	instance := serving(t, "9.9.9", answering(theFindings))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	if got := run(t, env, "search", "arch", "dec"); got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	asked := instance.Requests[len(instance.Requests)-1].URL.Query()
	if asked.Get("q") != "arch dec" {
		t.Fatalf("the words were not sent as one search: %q", asked.Get("q"))
	}
}

func TestSearchNarrowsToOneApplication(t *testing.T) {
	instance := serving(t, "9.9.9", answering(theFindings))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	if got := run(t, env, "search", "arch", "--application", "knowledge", "--limit", "5"); got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	asked := instance.Requests[len(instance.Requests)-1].URL.Query()
	if asked.Get("application") != "knowledge" || asked.Get("limit") != "5" {
		t.Fatalf("the narrowing did not travel: %v", asked)
	}
}

func TestSearchRefusesAWordThatIsNotAnApplicationWithoutAskingTheInstance(t *testing.T) {
	instance := serving(t, "9.9.9", answering(theFindings))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "search", "arch", "--application", "finance")

	if got.code != exit.Usage {
		t.Fatalf("want exit %d, got %d: %s", exit.Usage, got.code, got.stderr)
	}
	if len(instance.Requests) != 0 {
		t.Fatal("a typo in an application was sent to the instance")
	}
}

func TestSearchWithNothingToLookForIsAUsageMistake(t *testing.T) {
	instance := serving(t, "9.9.9", answering(theFindings))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "search")

	if got.code != exit.Usage {
		t.Fatalf("want exit %d, got %d: %s", exit.Usage, got.code, got.stderr)
	}
}

func TestSearchThatFindsNothingIsNotAFailure(t *testing.T) {
	instance := serving(t, "9.9.9", answering(`{"query":"x","has_more":false,"items":[]}`))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "search", "pomegranate")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if strings.TrimSpace(got.stdout) != "" {
		t.Fatalf("stdout is not empty: %q", got.stdout)
	}
	if !strings.Contains(got.stderr, "nothing matches") {
		t.Fatalf("nobody was told: %q", got.stderr)
	}
}

func TestSearchAsJsonIsTheObjectTheApiAnswered(t *testing.T) {
	instance := serving(t, "9.9.9", answering(theFindings))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "search", "arch", "--json")

	var body struct {
		Items []map[string]any `json:"items"`
	}
	if err := json.Unmarshal([]byte(got.stdout), &body); err != nil {
		t.Fatalf("not JSON: %v (%q)", err, got.stdout)
	}
	if len(body.Items) != 2 {
		t.Fatalf("the object is not the one the API answered: %v", body)
	}
}

func TestSearchCarriesARefusalThroughAsItsOwnExitCode(t *testing.T) {
	instance := serving(t, "9.9.9", refusing(
		http.StatusBadRequest,
		`{"type":"/problems/validation","title":"validation","status":400,
		  "detail":"q: Say what to look for."}`))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "search", "ab")

	if got.code != exit.Refused {
		t.Fatalf("want exit %d, got %d: %s", exit.Refused, got.code, got.stderr)
	}
}
