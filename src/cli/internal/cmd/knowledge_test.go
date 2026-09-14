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

// page is a body with what a round trip breaks first: a fenced block, a table
// whose columns are spaces, trailing whitespace, and characters outside ASCII.
const page = "# Die Architektur\n\nZeile mit Leerzeichen am Ende   \n\n" +
	"| eins | zwei |\n| --- | --- |\n| ja | 日本語 🙂 |\n\n```sh\npea knowledge tree\n```\n"

const reisenPageID = "0199f0c6-0000-7000-8000-00000000000a"

const bahnPageID = "0199f0c6-0000-7000-8000-00000000000b"

const thePageTree = `{"pages":[
 {"id":"0199f0c6-0000-7000-8000-00000000000a","title":"Reisen","parent":null,
  "created_at":"2026-09-14T08:00:00.000000Z","updated_at":"2026-09-14T08:00:00.000000Z"},
 {"id":"0199f0c6-0000-7000-8000-00000000000b","title":"Bahn","parent":"0199f0c6-0000-7000-8000-00000000000a",
  "created_at":"2026-09-14T08:10:00.000000Z","updated_at":"2026-09-14T08:10:00.000000Z"}]}`

const anEmptyPageTree = `{"pages":[]}`

var theBahnPage = `{"id":"0199f0c6-0000-7000-8000-00000000000b","title":"Bahn",
 "parent":"0199f0c6-0000-7000-8000-00000000000a",
 "markdown":` + mustJSON(page) + `,
 "created_at":"2026-09-14T08:10:00.000000Z","updated_at":"2026-09-14T08:10:00.000000Z"}`

const theHistory = `{"items":[
 {"id":"0199f0c6-0000-7000-8000-0000000000f1","title":"Bahn",
  "at":"2026-09-14T09:00:00.000000Z","by":{"kind":"agent","name":"the writing agent"}},
 {"id":"0199f0c6-0000-7000-8000-0000000000f2","title":"Die Bahn",
  "at":"2026-09-14T08:30:00.000000Z","by":{"kind":"owner","name":null}}]}`

func mustJSON(value string) string {
	encoded, err := json.Marshal(value)
	if err != nil {
		panic(err)
	}
	return string(encoded)
}

// theBase answers the two reads every verb makes, and nothing else.
func theBase(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Content-Type", "application/json")

	if strings.HasSuffix(r.URL.Path, "/api/knowledge/pages") {
		_, _ = w.Write([]byte(thePageTree))
		return
	}

	_, _ = w.Write([]byte(theBahnPage))
}

func TestKnowledgeTreePrintsEveryPageIndentedByDepth(t *testing.T) {
	instance := serving(t, "9.9.9", theBase)
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "knowledge", "tree")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	lines := strings.Split(strings.TrimSpace(got.stdout), "\n")
	if len(lines) != 2 {
		t.Fatalf("want two lines, got %q", got.stdout)
	}

	if !strings.HasSuffix(lines[0], "\tReisen") {
		t.Fatalf("the top page's line is %q", lines[0])
	}

	// The tree is a tree, and the indentation is what says so on a terminal.
	if !strings.HasSuffix(lines[1], "\t  Bahn") {
		t.Fatalf("the page under it is not indented: %q", lines[1])
	}
}

func TestKnowledgeTreeOfAnEmptyBaseWritesNothingToStdout(t *testing.T) {
	instance := serving(t, "9.9.9", answering(anEmptyPageTree))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "knowledge", "tree")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if got.stdout != "" {
		t.Fatalf("stdout is not empty: %q", got.stdout)
	}
	if !strings.Contains(got.stderr, "no pages") {
		t.Fatalf("stderr says nothing: %q", got.stderr)
	}
}

func TestKnowledgeShowWritesTheMarkdownAndNothingElse(t *testing.T) {
	instance := serving(t, "9.9.9", theBase)
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "knowledge", "show", "/Reisen/Bahn")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	// Byte for byte: `show > page.md` then `edit --text-file page.md` is a
	// round trip, and a heading in front of it would make it something else.
	if got.stdout != page {
		t.Fatalf("what came out is not the page: %q", got.stdout)
	}

	// A path is pea's convenience: the wire carries ids.
	for _, request := range instance.Requests {
		if strings.Contains(request.URL.Path, "Reisen") {
			t.Fatalf("a title went to the instance: %s", request.URL)
		}
	}
}

func TestKnowledgeFindsAPathTypedInAnotherCase(t *testing.T) {
	instance := serving(t, "9.9.9", theBase)
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	if got := run(t, env, "knowledge", "show", "/reisen/BAHN"); got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
}

func TestKnowledgeAPathThatNamesNothingIsExitThreeAndNamesTheSegment(t *testing.T) {
	instance := serving(t, "9.9.9", theBase)
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "knowledge", "show", "/Reisen/Flug")

	if got.code != exit.NotFound {
		t.Fatalf("want exit %d, got %d: %s", exit.NotFound, got.code, got.stderr)
	}
	if !strings.Contains(got.stderr, "Flug") {
		t.Fatalf("the sentence does not name the segment that failed: %q", got.stderr)
	}
}

func TestKnowledgeNewSendsTheTitleTheParentAndTheBody(t *testing.T) {
	var sent map[string]any

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodPost {
			_ = json.NewDecoder(r.Body).Decode(&sent)
			w.Header().Set("Content-Type", "application/json")
			w.WriteHeader(http.StatusCreated)
			_, _ = w.Write([]byte(theBahnPage))
			return
		}
		theBase(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := runWith(t, strings.NewReader(page), env, "knowledge", "new", "/Reisen/Flug", "--text-file", "-")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if sent["title"] != "Flug" || sent["parent"] != reisenPageID {
		t.Fatalf("what was sent is %v", sent)
	}

	// A trailing newline is the shell's; the newlines inside are the person's.
	if sent["markdown"] != strings.TrimRight(page, "\n") {
		t.Fatalf("the body did not survive: %q", sent["markdown"])
	}
	if strings.TrimSpace(got.stdout) != bahnPageID {
		t.Fatalf("stdout is not the id: %q", got.stdout)
	}
}

func TestKnowledgeNewWithNoBodyIsStillAPage(t *testing.T) {
	var sent map[string]any

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodPost {
			_ = json.NewDecoder(r.Body).Decode(&sent)
			w.Header().Set("Content-Type", "application/json")
			w.WriteHeader(http.StatusCreated)
			_, _ = w.Write([]byte(theBahnPage))
			return
		}
		theBase(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	// How a knowledge base usually starts a page.
	got := runWith(t, blocking{}, env, "knowledge", "new", "/Notizen")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if sent["markdown"] != "" || sent["parent"] != nil {
		t.Fatalf("what was sent is %v", sent)
	}
}

func TestKnowledgeEditCarriesForwardWhateverItWasNotGiven(t *testing.T) {
	var sent map[string]any
	var ifMatch string

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodPut {
			_ = json.NewDecoder(r.Body).Decode(&sent)
			ifMatch = r.Header.Get("If-Match")
			answering(theBahnPage)(w, r)
			return
		}
		theBase(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "knowledge", "edit", bahnPageID, "--title", "Die Bahn")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	// One write carries all three, because all three are the same row. The two
	// that were not given are what the instance already had.
	if sent["title"] != "Die Bahn" || sent["markdown"] != page || sent["parent"] != reisenPageID {
		t.Fatalf("what was sent is %v", sent)
	}
	if ifMatch != `"2026-09-14T08:10:00.000000Z"` {
		t.Fatalf("the version sent is %q", ifMatch)
	}
}

func TestKnowledgeEditWithNeitherFlagIsAUsageMistake(t *testing.T) {
	instance := serving(t, "9.9.9", theBase)
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "knowledge", "edit", bahnPageID)

	if got.code != exit.Usage {
		t.Fatalf("want exit %d, got %d: %s", exit.Usage, got.code, got.stderr)
	}
}

func TestKnowledgeEditOfAStaleReadIsExitSix(t *testing.T) {
	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodPut {
			refusing(http.StatusPreconditionFailed, `{"type":"/problems/stale",
 "title":"The object has changed since it was read","status":412,
 "detail":"The page has changed since it was read. Read it again."}`)(w, r)
			return
		}
		theBase(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := runWith(t, strings.NewReader("neu"), env,
		"knowledge", "edit", bahnPageID, "--text-file", "-")

	if got.code != exit.Stale {
		t.Fatalf("want exit %d, got %d: %s", exit.Stale, got.code, got.stderr)
	}
	if !strings.Contains(got.stderr, "Read it again") {
		t.Fatalf("the sentence does not say what to do: %q", got.stderr)
	}
}

func TestKnowledgeMoveUnderAnExistingPageKeepsTheTitle(t *testing.T) {
	var sent map[string]any

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodPut {
			_ = json.NewDecoder(r.Body).Decode(&sent)
			answering(theBahnPage)(w, r)
			return
		}
		theBase(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "knowledge", "mv", bahnPageID, "/Reisen")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if sent["title"] != "Bahn" || sent["parent"] != reisenPageID {
		t.Fatalf("what was sent is %v", sent)
	}
}

func TestKnowledgeMoveToAPathThatNamesNothingRenamesIt(t *testing.T) {
	var sent map[string]any

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodPut {
			_ = json.NewDecoder(r.Body).Decode(&sent)
			answering(theBahnPage)(w, r)
			return
		}
		theBase(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "knowledge", "mv", bahnPageID, "/Reisen/Zug")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if sent["title"] != "Zug" || sent["parent"] != reisenPageID {
		t.Fatalf("what was sent is %v", sent)
	}
}

func TestKnowledgeRemoveSaysItIsInTheTrashAndHowToGetItBack(t *testing.T) {
	var deleted string

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodDelete {
			deleted = r.URL.Path
			w.WriteHeader(http.StatusNoContent)
			return
		}
		theBase(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "knowledge", "rm", "/Reisen/Bahn")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if !strings.HasSuffix(deleted, "/api/knowledge/pages/"+bahnPageID) {
		t.Fatalf("what was deleted is %q", deleted)
	}
	if !strings.Contains(got.stderr, "trash restore knowledge") {
		t.Fatalf("the sentence does not say how to get it back: %q", got.stderr)
	}
}

func TestKnowledgeHistoryPrintsOneLinePerVersion(t *testing.T) {
	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if strings.HasSuffix(r.URL.Path, "/revisions") {
			answering(theHistory)(w, r)
			return
		}
		theBase(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "knowledge", "history", bahnPageID)

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	lines := strings.Split(strings.TrimSpace(got.stdout), "\n")
	if len(lines) != 2 {
		t.Fatalf("want two lines, got %q", got.stdout)
	}

	// Who ended a version is half of what somebody reading a history wants.
	if !strings.Contains(lines[0], "agent the writing agent") {
		t.Fatalf("the first line does not say who: %q", lines[0])
	}
	if !strings.Contains(lines[1], "the owner") {
		t.Fatalf("the second line does not say who: %q", lines[1])
	}
}

func TestKnowledgeHistoryWithARevisionWritesThatVersionsMarkdown(t *testing.T) {
	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if strings.Contains(r.URL.Path, "/revisions/") {
			answering(`{"id":"0199f0c6-0000-7000-8000-0000000000f1","title":"Bahn",
 "markdown":"was es einmal sagte","at":"2026-09-14T09:00:00.000000Z",
 "by":{"kind":"owner","name":null}}`)(w, r)
			return
		}
		theBase(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "knowledge", "history", bahnPageID,
		"--revision", "0199f0c6-0000-7000-8000-0000000000f1")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if got.stdout != "was es einmal sagte" {
		t.Fatalf("stdout is %q", got.stdout)
	}
}

func TestKnowledgeRecoverSaysWhatHappenedToWhatItReplaced(t *testing.T) {
	var recovered string
	var ifMatch string

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodPost {
			recovered = r.URL.Path
			ifMatch = r.Header.Get("If-Match")
			answering(theBahnPage)(w, r)
			return
		}
		theBase(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "knowledge", "recover", bahnPageID, "0199f0c6-0000-7000-8000-0000000000f1")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if !strings.HasSuffix(recovered, "/revisions/0199f0c6-0000-7000-8000-0000000000f1") {
		t.Fatalf("what was recovered is %q", recovered)
	}

	// The version it is guarded by is the page's, not the revision's.
	if ifMatch != `"2026-09-14T08:10:00.000000Z"` {
		t.Fatalf("the version sent is %q", ifMatch)
	}
	if !strings.Contains(got.stderr, "version of its own") {
		t.Fatalf("the sentence does not say history grew: %q", got.stderr)
	}
}

func TestKnowledgeRecoverWithSomethingThatIsNotAVersionIsAUsageMistake(t *testing.T) {
	instance := serving(t, "9.9.9", theBase)
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "knowledge", "recover", bahnPageID, "gestern")

	if got.code != exit.Usage {
		t.Fatalf("want exit %d, got %d: %s", exit.Usage, got.code, got.stderr)
	}
}

func TestKnowledgeExportWritesTheZipByteForByte(t *testing.T) {
	// A zip nothing here parses: what is under test is that the bytes arrive
	// unchanged and that a body which is not JSON is still a success.
	zip := []byte{'P', 'K', 0x03, 0x04, 0x00, 0xff, 0x00}

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/zip")
		w.Header().Set("Content-Disposition", `attachment; filename="knowledge-2026-09-14.zip"`)
		_, _ = w.Write(zip)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "knowledge", "export", "--out", "-")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if got.stdout != string(zip) {
		t.Fatalf("what came down is not what was sent: %v", []byte(got.stdout))
	}
}

func TestKnowledgeExportWritesAFileAndRefusesToOverwriteOne(t *testing.T) {
	zip := []byte{'P', 'K', 0x03, 0x04}

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/zip")
		_, _ = w.Write(zip)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	out := filepath.Join(t.TempDir(), "knowledge.zip")

	if got := run(t, env, "knowledge", "export", "--out", out); got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	written, err := os.ReadFile(out)
	if err != nil || string(written) != string(zip) {
		t.Fatalf("the file on disk is %v (%v)", written, err)
	}

	// Nothing in pea overwrites a file it did not make.
	if again := run(t, env, "knowledge", "export", "--out", out); again.code != exit.Usage {
		t.Fatalf("want exit %d, got %d: %s", exit.Usage, again.code, again.stderr)
	}
}

func TestKnowledgeJSONIsTheInstancesOwnDocument(t *testing.T) {
	instance := serving(t, "9.9.9", theBase)
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "--json", "knowledge", "show", bahnPageID)

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	var answered map[string]any
	if err := json.Unmarshal([]byte(got.stdout), &answered); err != nil {
		t.Fatalf("stdout is not JSON: %v (%q)", err, got.stdout)
	}
	if answered["markdown"] != page {
		t.Fatalf("the page lost what the instance answered: %v", answered["title"])
	}
}
