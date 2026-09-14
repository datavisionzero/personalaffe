package cmd_test

import (
	"encoding/json"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/datavisionzero/personalaffe/src/cli/internal/client"
	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/exit"
)

// note is a piece of somebody's text with the two things in it that break
// first: characters outside ASCII, and newlines in the middle.
const note = "Größe: 5 m²\n\nZeile zwei — 日本語 🙂\n\tund ein Tabulator"

const theEntry = `{"id":"0199f0c4-0000-7000-8000-000000000001",
 "text":"Größe: 5 m²\n\nZeile zwei — 日本語 🙂\n\tund ein Tabulator",
 "pinned":false,
 "created_at":"2026-09-14T08:30:00.123456Z",
 "updated_at":"2026-09-14T08:30:00.123456Z",
 "expires_at":"2026-09-21T08:30:00.123456Z"}`

const thePinnedEntry = `{"id":"0199f0c4-0000-7000-8000-000000000002",
 "text":"the wifi password","pinned":true,
 "created_at":"2026-09-01T08:30:00.000000Z",
 "updated_at":"2026-09-01T08:30:00.000000Z",
 "expires_at":null}`

const theScratchpad = `{"items":[` + theEntry + `,` + thePinnedEntry + `],"has_more":false}`

const anEmptyScratchpad = `{"items":[],"has_more":false}`

const entryID = "0199f0c4-0000-7000-8000-000000000001"

func TestScratchpadAddSendsTheTextFromStdinAndPrintsTheId(t *testing.T) {
	var sent map[string]any

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		_ = json.NewDecoder(r.Body).Decode(&sent)
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(http.StatusCreated)
		_, _ = w.Write([]byte(theEntry))
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	// The trailing newline a shell adds, and nothing else changed.
	got := runWith(t, strings.NewReader(note+"\n"), env, "scratchpad", "add", "--text-file", "-")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if sent["text"] != note {
		t.Fatalf("what went in is not what was typed: %q", sent["text"])
	}
	if strings.TrimSpace(got.stdout) != entryID {
		t.Fatalf("stdout is not the id alone: %q", got.stdout)
	}
	if !strings.Contains(got.stderr, "2026-09-21") {
		t.Fatalf("nothing was said about when it goes: %q", got.stderr)
	}
}

func TestScratchpadAddTakesTheSameTextFromAFile(t *testing.T) {
	var sent map[string]any

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		_ = json.NewDecoder(r.Body).Decode(&sent)
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(http.StatusCreated)
		_, _ = w.Write([]byte(theEntry))
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	path := filepath.Join(t.TempDir(), "note.txt")
	if err := os.WriteFile(path, []byte(note+"\n"), 0o600); err != nil {
		t.Fatal(err)
	}

	// A stdin nothing ever writes to: a verb given a file must not read one.
	got := runWith(t, blocking{}, env, "scratchpad", "add", "--text-file", path, "--pinned")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if sent["text"] != note {
		t.Fatalf("a file and stdin disagree: %q", sent["text"])
	}
	if sent["pinned"] != true {
		t.Fatalf("--pinned did not travel: %v", sent)
	}
}

func TestScratchpadShowWritesTheTextAndNothingElse(t *testing.T) {
	instance := serving(t, "9.9.9", answering(theEntry))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "scratchpad", "show", entryID)

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	// `pea scratchpad show ID > note.txt` is the round trip of
	// `add --text-file`, so what lands on stdout is the bytes and no heading.
	if got.stdout != note {
		t.Fatalf("the round trip changed the text: %q", got.stdout)
	}
}

func TestScratchpadListIsOneLinePerEntryAndSaysWhenEachGoes(t *testing.T) {
	instance := serving(t, "9.9.9", answering(theScratchpad))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "scratchpad", "list")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	lines := strings.Split(strings.TrimRight(got.stdout, "\n"), "\n")
	if len(lines) != 2 {
		t.Fatalf("want a line per entry, got %d: %q", len(lines), got.stdout)
	}

	// Tab separated, so that `cut` and `awk` work on it.
	columns := strings.Split(lines[0], "\t")
	if len(columns) != 5 {
		t.Fatalf("want five columns, got %d: %q", len(columns), lines[0])
	}
	if columns[0] != entryID {
		t.Fatalf("the first column is not the id: %q", columns[0])
	}
	if columns[3] != "2026-09-21T08:30:00Z" {
		t.Fatalf("the expiry column is wrong: %q", columns[3])
	}

	// One line of it, and never the newlines: a list whose rows wrap is not a
	// list.
	if strings.Contains(columns[4], "Zeile zwei") {
		t.Fatalf("the list shows more than the first line: %q", columns[4])
	}

	// A pinned entry has no expiry at all, and the column says so rather than
	// being blank.
	if !strings.Contains(lines[1], "pinned") || !strings.Contains(lines[1], "never") {
		t.Fatalf("a pinned entry does not read as one: %q", lines[1])
	}
}

func TestAnEmptyScratchpadWritesNothingToStdout(t *testing.T) {
	instance := serving(t, "9.9.9", answering(anEmptyScratchpad))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "scratchpad", "list")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	// A pipeline reading it gets nothing rather than a sentence.
	if got.stdout != "" {
		t.Fatalf("stdout is not empty: %q", got.stdout)
	}
	if got.stderr == "" {
		t.Fatal("nothing was said to the person")
	}
}

func TestEveryVerbWithAnObjectAnswersTheApisOwnObjectAsJson(t *testing.T) {
	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		if r.Method == http.MethodPost {
			w.WriteHeader(http.StatusCreated)
		}
		if strings.HasSuffix(r.URL.Path, "/entries") && r.Method == http.MethodGet {
			_, _ = w.Write([]byte(theScratchpad))
			return
		}
		_, _ = w.Write([]byte(theEntry))
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	for _, args := range [][]string{
		{"scratchpad", "list", "--json"},
		{"scratchpad", "show", entryID, "--json"},
		{"scratchpad", "pin", entryID, "--json"},
		{"scratchpad", "unpin", entryID, "--json"},
	} {
		got := run(t, env, args...)

		if got.code != exit.OK {
			t.Fatalf("%v: want exit 0, got %d: %s", args, got.code, got.stderr)
		}

		var body map[string]any
		if err := json.Unmarshal([]byte(got.stdout), &body); err != nil {
			t.Fatalf("%v: not JSON: %v (%q)", args, err, got.stdout)
		}
	}
}

func TestPinReadsTheEntryAndSendsTheVersionItFound(t *testing.T) {
	var write *http.Request
	var sent map[string]any

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		if r.Method == http.MethodGet {
			_, _ = w.Write([]byte(theEntry))
			return
		}
		write = r.Clone(r.Context())
		_ = json.NewDecoder(r.Body).Decode(&sent)
		_, _ = w.Write([]byte(thePinnedEntry))
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "scratchpad", "pin", entryID)

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if write == nil {
		t.Fatal("nothing was written")
	}

	// The guard, made by pea rather than by whoever is calling it: an agent
	// should not have to hold a timestamp.
	want := client.EntityTag(mustTime(t, "2026-09-14T08:30:00.123456Z"))
	if write.Header.Get("If-Match") != want {
		t.Fatalf("the version sent is %q, want %q", write.Header.Get("If-Match"), want)
	}

	// One write carries the text and the pin together, so the text it found
	// goes back with it rather than being emptied.
	if sent["pinned"] != true || sent["text"] != note {
		t.Fatalf("the write did not carry the entry: %v", sent)
	}
}

func TestEditCarriesThePinForwardWhenNobodySaidOtherwise(t *testing.T) {
	var sent map[string]any

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		if r.Method == http.MethodGet {
			_, _ = w.Write([]byte(thePinnedEntry))
			return
		}
		_ = json.NewDecoder(r.Body).Decode(&sent)
		_, _ = w.Write([]byte(thePinnedEntry))
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := runWith(t, strings.NewReader("something else\n"), env,
		"scratchpad", "edit", entryID, "--text-file", "-")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	// Changing the text must not silently unpin the entry.
	if sent["pinned"] != true {
		t.Fatalf("the pin was not carried forward: %v", sent)
	}
	if sent["text"] != "something else" {
		t.Fatalf("the new text did not arrive: %v", sent)
	}
}

func TestRemoveIsGuardedAndSaysThatItIsPermanent(t *testing.T) {
	var write *http.Request

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodGet {
			w.Header().Set("Content-Type", "application/json")
			_, _ = w.Write([]byte(theEntry))
			return
		}
		write = r.Clone(r.Context())
		w.WriteHeader(http.StatusNoContent)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "scratchpad", "rm", entryID)

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if write == nil || write.Header.Get("If-Match") == "" {
		t.Fatal("the delete was not guarded")
	}
	if !strings.Contains(got.stderr, "Trash") {
		t.Fatalf("nothing said that this one is not recoverable: %q", got.stderr)
	}
}

func TestTheInstancesRefusalsBecomeTheCodesAScriptBranchesOn(t *testing.T) {
	for _, refusal := range []struct {
		status   int
		document string
		want     int
	}{
		{http.StatusPreconditionFailed, `{"type":"/problems/stale","title":"x","status":412}`, exit.Stale},
		{http.StatusConflict, `{"type":"/problems/disabled","title":"x","status":409}`, exit.Conflict},
		{http.StatusForbidden, `{"type":"/problems/forbidden","title":"x","status":403}`, exit.Denied},
		{http.StatusNotFound, `{"type":"/problems/not-found","title":"x","status":404}`, exit.NotFound},
		{http.StatusBadRequest, `{"type":"/problems/validation","title":"x","status":400}`, exit.Refused},
	} {
		instance := serving(t, "9.9.9", refusing(refusal.status, refusal.document))
		env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

		// The read is the first request every one of these makes, so the
		// refusal lands whichever verb it was.
		for _, args := range [][]string{
			{"scratchpad", "list"},
			{"scratchpad", "show", entryID},
			{"scratchpad", "pin", entryID},
			{"scratchpad", "rm", entryID},
		} {
			got := run(t, env, args...)

			if got.code != refusal.want {
				t.Errorf("%v against %d: want exit %d, got %d (%s)",
					args, refusal.status, refusal.want, got.code, got.stderr)
			}
		}
	}
}

func TestAddWithoutAFileIsAUsageMistakeAndNotARequest(t *testing.T) {
	instance := serving(t, "9.9.9", answering(theEntry))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "scratchpad", "add")

	if got.code != exit.Usage {
		t.Fatalf("want exit %d, got %d: %s", exit.Usage, got.code, got.stderr)
	}
	if len(instance.Requests) != 0 {
		t.Fatalf("a usage mistake reached the instance: %d request(s)", len(instance.Requests))
	}
}

func TestAnIdThatIsNotOneIsAUsageMistakeNamingTheVerbThatPrintsThem(t *testing.T) {
	instance := serving(t, "9.9.9", answering(theEntry))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "scratchpad", "show", "not-an-id")

	if got.code != exit.Usage {
		t.Fatalf("want exit %d, got %d: %s", exit.Usage, got.code, got.stderr)
	}
	if !strings.Contains(got.stderr, "scratchpad list") {
		t.Fatalf("the refusal does not say where ids come from: %q", got.stderr)
	}
}

// mustTime is a moment the contract spells, parsed once so that a test can ask
// for the entity tag pea would have sent.
func mustTime(t *testing.T, moment string) time.Time {
	t.Helper()

	parsed, err := time.Parse(time.RFC3339Nano, moment)
	if err != nil {
		t.Fatalf("not a moment: %v", err)
	}

	return parsed
}

func TestThereIsNoVerbThatEmptiesTheScratchpad(t *testing.T) {
	// A single verb that destroys everything is one typo away from being the
	// thing this product is sorry about. A loop over `list` is a script
	// anybody can write.
	for _, args := range [][]string{
		{"scratchpad", "empty"},
		{"scratchpad", "purge"},
		{"scratchpad", "clear"},
	} {
		if got := run(t, environment(t, nil), args...); got.code != exit.Usage {
			t.Errorf("%v: want exit %d, got %d", args, exit.Usage, got.code)
		}
	}
}
