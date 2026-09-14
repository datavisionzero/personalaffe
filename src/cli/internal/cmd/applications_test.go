package cmd_test

import (
	"encoding/json"
	"strings"
	"testing"

	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/exit"
)

const theApplications = `{"items":[
  {"application":"scratchpad","enabled":true,"permission":"read_write",
   "updated_at":"2026-09-14T15:30:08.000000Z"},
  {"application":"knowledge","enabled":false,"permission":"read",
   "updated_at":"2026-09-14T15:31:00.000000Z"},
  {"application":"tasks","enabled":true,"permission":"none",
   "updated_at":"2026-09-14T15:30:08.000000Z"},
  {"application":"files","enabled":true,"permission":"read_write",
   "updated_at":"2026-09-14T15:30:08.000000Z"}]}`

func TestApplicationsSaysWhichAreOnAndWhatThisCredentialReaches(t *testing.T) {
	instance := serving(t, "9.9.9", answering(theApplications))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "applications")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	lines := strings.Split(strings.TrimSpace(got.stdout), "\n")
	if len(lines) != 4 {
		t.Fatalf("want a line per application, got %q", got.stdout)
	}

	// The switch is a word rather than a blank: the line is read by a person.
	if lines[0] != "scratchpad\ton\tread_write" {
		t.Fatalf("the first line is %q", lines[0])
	}
	if lines[1] != "knowledge\toff\tread" {
		t.Fatalf("a switched-off application is not said so: %q", lines[1])
	}

	// All four, including the one this credential cannot reach: that is the
	// answer to why an operation in it was refused.
	if !strings.Contains(got.stdout, "tasks\ton\tnone") {
		t.Fatalf("an application it cannot reach is missing: %q", got.stdout)
	}
}

func TestApplicationsAsJsonIsTheObjectTheApiAnswered(t *testing.T) {
	instance := serving(t, "9.9.9", answering(theApplications))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "applications", "--json")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	var body struct {
		Items []map[string]any `json:"items"`
	}
	if err := json.Unmarshal([]byte(got.stdout), &body); err != nil {
		t.Fatalf("not JSON: %v (%q)", err, got.stdout)
	}
	if len(body.Items) != 4 {
		t.Fatalf("the object is not the one the API answered: %v", body)
	}
}

func TestApplicationsWithNoCredentialAnywhereIsAUsageMistake(t *testing.T) {
	instance := serving(t, "9.9.9", answering(theApplications))
	env := environment(t, map[string]string{config.EnvURL: instance.URL})

	got := run(t, env, "applications")

	if got.code != exit.Usage {
		t.Fatalf("want exit %d, got %d: %s", exit.Usage, got.code, got.stderr)
	}
	if len(instance.Requests) != 0 {
		t.Fatal("pea asked the instance without a credential to present")
	}
}

func TestApplicationsHasNoVerbThatSwitchesOne(t *testing.T) {
	instance := serving(t, "9.9.9", answering(theApplications))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	// Switching one is the owner's alone and pea holds agent access. A verb
	// here would be a verb that can only ever be refused.
	got := run(t, env, "applications", "disable", "knowledge")

	if got.code != exit.Usage {
		t.Fatalf("want exit %d, got %d: %s", exit.Usage, got.code, got.stderr)
	}
}
