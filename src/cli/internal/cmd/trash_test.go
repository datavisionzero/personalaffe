package cmd_test

import (
	"encoding/json"
	"net/http"
	"strings"
	"testing"

	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/exit"
)

const inTheTrash = `{"items":[
  {"application":"knowledge","id":"0199f0c4-0000-7000-8000-000000000001","name":"architecture",
   "where":"/notes","deleted_at":"2026-09-12T19:02:11.881000Z",
   "deleted_by":{"kind":"agent","name":"the laptop agent"},
   "expires_at":"2026-10-12T19:02:11.881000Z","updated_at":"2026-09-12T19:02:11.881000Z"}],
 "has_more":false}`

const restored = `{"application":"knowledge","id":"0199f0c4-0000-7000-8000-000000000001",
 "name":"architecture","where":"/notes","moved_to_the_root":false}`

func TestTrashListSaysWhatIsInItAndWhoPutItThere(t *testing.T) {
	instance := serving(t, "9.9.9", answering(inTheTrash))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "trash", "list")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	for _, said := range []string{"knowledge", "architecture", "/notes", "the laptop agent", "2026-10-12"} {
		if !strings.Contains(got.stdout, said) {
			t.Fatalf("the list does not say %q: %q", said, got.stdout)
		}
	}
}

func TestTrashListAsJsonIsTheObjectTheApiAnswered(t *testing.T) {
	instance := serving(t, "9.9.9", answering(inTheTrash))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "trash", "list", "--json")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	var body map[string]any
	if err := json.Unmarshal([]byte(got.stdout), &body); err != nil {
		t.Fatalf("not JSON: %v (%q)", err, got.stdout)
	}
	if body["has_more"] != false {
		t.Fatalf("the object is not the one the API answered: %v", body)
	}
}

func TestTrashRestoreReadsTheVersionItselfAndSendsItBack(t *testing.T) {
	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		if r.Method == http.MethodGet {
			_, _ = w.Write([]byte(inTheTrash))
			return
		}
		_, _ = w.Write([]byte(restored))
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "trash", "restore", "knowledge", "0199f0c4-0000-7000-8000-000000000001")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	// The read happened, and the write carried what it read: an agent never has
	// to hold a timestamp of its own.
	if len(instance.Requests) != 2 {
		t.Fatalf("want a read and a write, got %d requests", len(instance.Requests))
	}

	sent := instance.Requests[1].Header.Get("If-Match")
	if sent != `"2026-09-12T19:02:11.881000Z"` {
		t.Fatalf("the write did not send the version the list gave it: %q", sent)
	}
}

func TestTrashRestoreTakesAVersionItIsGivenWithoutReadingFirst(t *testing.T) {
	instance := serving(t, "9.9.9", answering(restored))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "trash", "restore", "knowledge", "0199f0c4-0000-7000-8000-000000000001",
		"--if-match", `"2026-09-12T19:02:11.881000Z"`)

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if len(instance.Requests) != 1 {
		t.Fatalf("want one request, got %d", len(instance.Requests))
	}
}

func TestTrashRestoreOfSomethingChangedSinceIsExitSix(t *testing.T) {
	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodGet {
			w.Header().Set("Content-Type", "application/json")
			_, _ = w.Write([]byte(inTheTrash))
			return
		}
		w.Header().Set("Content-Type", "application/problem+json")
		w.WriteHeader(http.StatusPreconditionFailed)
		_, _ = w.Write([]byte(
			`{"type":"/problems/stale","title":"The object has changed since it was read","status":412,` +
				`"detail":"The page has changed since it was read."}`))
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "trash", "restore", "knowledge", "0199f0c4-0000-7000-8000-000000000001")

	if got.code != exit.Stale {
		t.Fatalf("want exit %d, got %d: %s", exit.Stale, got.code, got.stderr)
	}
}

func TestTrashRestoreSaysWhenSomethingWasMovedToTheRoot(t *testing.T) {
	instance := serving(t, "9.9.9", answering(
		`{"application":"knowledge","id":"0199f0c4-0000-7000-8000-000000000001",` +
			`"name":"architecture","where":null,"moved_to_the_root":true}`))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "trash", "restore", "knowledge", "0199f0c4-0000-7000-8000-000000000001",
		"--if-match", `"2026-09-12T19:02:11.881000Z"`)

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if !strings.Contains(got.stderr, "root") {
		t.Fatalf("nothing said the content had been moved: %q", got.stderr)
	}
}

func TestTrashRestoreOfSomethingThatIsNotThereIsExitThree(t *testing.T) {
	instance := serving(t, "9.9.9", answering(`{"items":[],"has_more":false}`))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "trash", "restore", "knowledge", "0199f0c4-0000-7000-8000-000000000009")

	if got.code != exit.NotFound {
		t.Fatalf("want exit %d, got %d: %s", exit.NotFound, got.code, got.stderr)
	}

	// And nothing was written: the version could not be read, so there was
	// nothing to send.
	if len(instance.Requests) != 1 {
		t.Fatalf("want the read alone, got %d requests", len(instance.Requests))
	}
}

func TestATypoInAnApplicationIsAUsageMistakeAndNotARoundTrip(t *testing.T) {
	instance := serving(t, "9.9.9", answering(inTheTrash))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "trash", "restore", "knowlege", "0199f0c4-0000-7000-8000-000000000001")

	if got.code != exit.Usage {
		t.Fatalf("want exit %d, got %d: %s", exit.Usage, got.code, got.stderr)
	}
	if !strings.Contains(got.stderr, "scratchpad, knowledge, tasks, files") {
		t.Fatalf("the refusal does not say what the applications are: %q", got.stderr)
	}
	if len(instance.Requests) != 0 {
		t.Fatalf("a typo reached the instance: %d requests", len(instance.Requests))
	}
}

func TestPeaHasNoVerbThatDestroysAnything(t *testing.T) {
	env := environment(t, map[string]string{config.EnvURL: "https://workspace.example.com"})

	for _, verb := range [][]string{
		{"trash", "purge"},
		{"trash", "empty"},
		{"trash", "remove"},
	} {
		got := run(t, env, verb...)
		if got.code != exit.Usage {
			t.Fatalf("`pea %s` exists: exit %d", strings.Join(verb, " "), got.code)
		}
	}
}
