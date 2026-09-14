package cmd_test

import (
	"bytes"
	"encoding/json"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/exit"
)

// theBytes is a file with what breaks first in one: bytes that are not text, a
// NUL in the middle, and a last byte that is not a newline.
var theBytes = []byte{0x00, 0x01, 0xff, 'p', 'd', 'f', 0x00, 0x7f, 0xfe}

const theTopOfTheTree = `{"chain":[],
 "folders":[{"id":"0199f0c4-0000-7000-8000-00000000000a","name":"Reisen","parent":null,
   "created_at":"2026-09-14T08:00:00.000000Z","updated_at":"2026-09-14T08:00:00.000000Z"}],
 "files":[{"id":"0199f0c4-0000-7000-8000-00000000000f","name":"plan.md","folder":null,
   "size":12,"media_type":"text/markdown",
   "created_at":"2026-09-14T08:10:00.000000Z","updated_at":"2026-09-14T08:10:00.000000Z"}],
 "used_bytes":12,"max_file_bytes":67108864,"max_total_bytes":5368709120}`

const theFolder = `{"chain":[{"id":"0199f0c4-0000-7000-8000-00000000000a","name":"Reisen","parent":null,
   "created_at":"2026-09-14T08:00:00.000000Z","updated_at":"2026-09-14T08:00:00.000000Z"}],
 "folders":[],
 "files":[{"id":"0199f0c4-0000-7000-8000-00000000000b","name":"bahn.pdf",
   "folder":"0199f0c4-0000-7000-8000-00000000000a","size":9,"media_type":"application/pdf",
   "created_at":"2026-09-14T08:20:00.000000Z","updated_at":"2026-09-14T08:20:00.000000Z"}],
 "used_bytes":21,"max_file_bytes":67108864,"max_total_bytes":5368709120}`

const anEmptyFolder = `{"chain":[{"id":"0199f0c4-0000-7000-8000-00000000000a","name":"Reisen","parent":null,
   "created_at":"2026-09-14T08:00:00.000000Z","updated_at":"2026-09-14T08:00:00.000000Z"}],
 "folders":[],"files":[],
 "used_bytes":0,"max_file_bytes":67108864,"max_total_bytes":5368709120}`

const theStoredFile = `{"id":"0199f0c4-0000-7000-8000-00000000000b","name":"bahn.pdf",
 "folder":"0199f0c4-0000-7000-8000-00000000000a","size":9,"media_type":"application/pdf",
 "created_at":"2026-09-14T08:20:00.000000Z","updated_at":"2026-09-14T08:20:00.000000Z"}`

const reisenID = "0199f0c4-0000-7000-8000-00000000000a"

const bahnID = "0199f0c4-0000-7000-8000-00000000000b"

// theTree answers the two listings a path walk asks for, and nothing else.
func theTree(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Content-Type", "application/json")

	if r.URL.Query().Get("folder") == reisenID {
		_, _ = w.Write([]byte(theFolder))
		return
	}

	_, _ = w.Write([]byte(theTopOfTheTree))
}

func TestFilesListPrintsOneLinePerEntryWithFoldersFirst(t *testing.T) {
	instance := serving(t, "9.9.9", theTree)
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "files", "ls")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	lines := strings.Split(strings.TrimSpace(got.stdout), "\n")
	if len(lines) != 2 {
		t.Fatalf("want two lines, got %q", got.stdout)
	}

	// Tab separated, so that cut and awk work on it and a terminal still lines
	// it up. A folder has no size, and says so rather than saying nothing.
	folder := strings.Split(lines[0], "\t")
	if len(folder) != 5 || folder[1] != "folder" || folder[2] != "-" || folder[4] != "Reisen" {
		t.Fatalf("the folder's line is %q", lines[0])
	}

	file := strings.Split(lines[1], "\t")
	if file[1] != "file" || file[2] != "12" || file[4] != "plan.md" {
		t.Fatalf("the file's line is %q", lines[1])
	}
}

func TestFilesListWalksAPathASegmentAtATime(t *testing.T) {
	instance := serving(t, "9.9.9", theTree)
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "files", "ls", "/Reisen")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if !strings.Contains(got.stdout, "bahn.pdf") {
		t.Fatalf("the folder's contents are not there: %q", got.stdout)
	}

	// The wire carries ids. A path is pea's convenience and the instance never
	// sees one.
	for _, request := range instance.Requests {
		if strings.Contains(request.URL.RawQuery, "Reisen") {
			t.Fatalf("a name went to the instance: %s", request.URL)
		}
	}
}

func TestFilesListFindsAPathTypedInAnotherCase(t *testing.T) {
	instance := serving(t, "9.9.9", theTree)
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	// Names in a folder are one each whatever their capitals (docs/api.md), so
	// a path typed the way somebody remembers it finds what it means.
	got := run(t, env, "files", "ls", "/reisen")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
}

func TestFilesListOfAnEmptyFolderWritesNothingToStdout(t *testing.T) {
	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		if r.URL.Query().Get("folder") == reisenID {
			_, _ = w.Write([]byte(anEmptyFolder))
			return
		}
		_, _ = w.Write([]byte(theTopOfTheTree))
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "files", "ls", "/Reisen")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	// A pipeline reading this gets nothing rather than a sentence.
	if got.stdout != "" {
		t.Fatalf("stdout is not empty: %q", got.stdout)
	}
	if !strings.Contains(got.stderr, "empty") {
		t.Fatalf("stderr says nothing: %q", got.stderr)
	}
}

func TestFilesListOfSomethingThatIsNotThereIsExitThreeAndNamesTheSegment(t *testing.T) {
	instance := serving(t, "9.9.9", theTree)
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "files", "ls", "/Reisen/2027")

	if got.code != exit.NotFound {
		t.Fatalf("want exit %d, got %d: %s", exit.NotFound, got.code, got.stderr)
	}
	if !strings.Contains(got.stderr, "2027") {
		t.Fatalf("the sentence does not name the segment that failed: %q", got.stderr)
	}
}

func TestFilesPutSendsTheBytesFromStdinAndPrintsTheId(t *testing.T) {
	var sent []byte
	var contentType string
	var query string

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodPost {
			sent, _ = readAll(r)
			contentType = r.Header.Get("Content-Type")
			query = r.URL.RawQuery
			w.Header().Set("Content-Type", "application/json")
			w.WriteHeader(http.StatusCreated)
			_, _ = w.Write([]byte(theStoredFile))
			return
		}
		theTree(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := runWith(t, bytes.NewReader(theBytes), env, "files", "put", "--file", "-", "--name", "bahn.pdf")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	// Byte for byte: nothing is added, nothing trimmed, and the last byte is
	// not a newline somebody dropped.
	if !bytes.Equal(sent, theBytes) {
		t.Fatalf("what went up is not what was piped in: %v", sent)
	}
	if contentType != "application/octet-stream" {
		t.Fatalf("the body is not declared as bytes: %q", contentType)
	}
	if !strings.Contains(query, "name=bahn.pdf") {
		t.Fatalf("the name did not travel: %q", query)
	}
	if strings.TrimSpace(got.stdout) != bahnID {
		t.Fatalf("stdout is not the id: %q", got.stdout)
	}
}

func TestFilesPutFromStdinWithoutANameIsAUsageMistake(t *testing.T) {
	instance := serving(t, "9.9.9", theTree)
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := runWith(t, bytes.NewReader(theBytes), env, "files", "put", "--file", "-")

	// There is nothing to take a name from, and guessing one would be pea
	// naming the owner's file.
	if got.code != exit.Usage {
		t.Fatalf("want exit %d, got %d: %s", exit.Usage, got.code, got.stderr)
	}
	if !strings.Contains(got.stderr, "--name") {
		t.Fatalf("the sentence does not say what to do: %q", got.stderr)
	}
}

func TestFilesPutTakesTheNameFromTheFileAndTheFolderFromTo(t *testing.T) {
	var query string

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodPost {
			query = r.URL.RawQuery
			w.Header().Set("Content-Type", "application/json")
			w.WriteHeader(http.StatusCreated)
			_, _ = w.Write([]byte(theStoredFile))
			return
		}
		theTree(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	path := filepath.Join(t.TempDir(), "Reisekosten 2026.pdf")
	if err := os.WriteFile(path, theBytes, 0o600); err != nil {
		t.Fatal(err)
	}

	got := run(t, env, "files", "put", "--file", path, "--to", "/Reisen")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if !strings.Contains(query, "Reisekosten") {
		t.Fatalf("the base name did not become the name: %q", query)
	}
	if !strings.Contains(query, "folder="+reisenID) {
		t.Fatalf("the folder did not travel as an id: %q", query)
	}
}

func TestFilesPutOverTheLimitIsExitFourAndSaysWhichLimit(t *testing.T) {
	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodPost {
			refusing(http.StatusRequestEntityTooLarge, `{"type":"/problems/too-large",
 "title":"That is larger than this instance will store","status":413,
 "detail":"This file is larger than the 64 MiB one file may be on this instance.",
 "limit_bytes":67108864}`)(w, r)
			return
		}
		theTree(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := runWith(t, bytes.NewReader(theBytes), env, "files", "put", "--file", "-", "--name", "big.bin")

	// A write the instance would not take, like a validation refusal: the
	// script's move is to look at what it said and send something else.
	if got.code != exit.Refused {
		t.Fatalf("want exit %d, got %d: %s", exit.Refused, got.code, got.stderr)
	}
	if !strings.Contains(got.stderr, "64 MiB") {
		t.Fatalf("the sentence does not name the limit: %q", got.stderr)
	}
}

func TestFilesGetWritesTheBytesToStdoutByteForByte(t *testing.T) {
	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if strings.HasSuffix(r.URL.Path, "/content") {
			w.Header().Set("Content-Type", "application/pdf")
			w.Header().Set("Content-Disposition", `attachment; filename="bahn.pdf"`)
			_, _ = w.Write(theBytes)
			return
		}
		if strings.Contains(r.URL.Path, "/api/files/") {
			answering(theStoredFile)(w, r)
			return
		}
		theTree(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "files", "get", bahnID, "--out", "-")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	// `pea files get ID --out -` is the round trip of `put --file -`: a
	// download whose body is not JSON is still a success, and nothing is added
	// on the way out.
	if got.stdout != string(theBytes) {
		t.Fatalf("what came down is not what was stored: %v", []byte(got.stdout))
	}
}

func TestFilesGetWritesAFileAndRefusesToOverwriteOne(t *testing.T) {
	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if strings.HasSuffix(r.URL.Path, "/content") {
			w.Header().Set("Content-Type", "application/pdf")
			_, _ = w.Write(theBytes)
			return
		}
		if strings.Contains(r.URL.Path, "/api/files/") {
			answering(theStoredFile)(w, r)
			return
		}
		theTree(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	out := filepath.Join(t.TempDir(), "bahn.pdf")

	if got := run(t, env, "files", "get", bahnID, "--out", out); got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	written, err := os.ReadFile(out)
	if err != nil || !bytes.Equal(written, theBytes) {
		t.Fatalf("the file on disk is %v (%v)", written, err)
	}

	// Nothing in pea overwrites a file it did not make. A download that
	// silently replaced something would be the one destructive thing a read
	// can do.
	again := run(t, env, "files", "get", bahnID, "--out", out)
	if again.code != exit.Usage {
		t.Fatalf("want exit %d, got %d: %s", exit.Usage, again.code, again.stderr)
	}
}

func TestFilesGetOfAFolderSaysAFolderHasNoBytes(t *testing.T) {
	instance := serving(t, "9.9.9", theTree)
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "files", "get", "/Reisen", "--out", "-")

	if got.code != exit.Refused {
		t.Fatalf("want exit %d, got %d: %s", exit.Refused, got.code, got.stderr)
	}
	if got.stdout != "" {
		t.Fatalf("something went to stdout: %q", got.stdout)
	}
}

func TestFilesMkdirMakesOneFolderAndPrintsItsId(t *testing.T) {
	var sent map[string]any

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodPost {
			_ = json.NewDecoder(r.Body).Decode(&sent)
			w.Header().Set("Content-Type", "application/json")
			w.WriteHeader(http.StatusCreated)
			_, _ = w.Write([]byte(`{"id":"0199f0c4-0000-7000-8000-00000000001a","name":"Belege",
 "parent":"0199f0c4-0000-7000-8000-00000000000a",
 "created_at":"2026-09-14T09:00:00.000000Z","updated_at":"2026-09-14T09:00:00.000000Z"}`))
			return
		}
		theTree(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "files", "mkdir", "/Reisen/Belege")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if sent["name"] != "Belege" || sent["parent"] != reisenID {
		t.Fatalf("what was sent is %v", sent)
	}
	if strings.TrimSpace(got.stdout) != "0199f0c4-0000-7000-8000-00000000001a" {
		t.Fatalf("stdout is not the id: %q", got.stdout)
	}
}

func TestFilesMkdirWithoutParentsRefusesAMissingFolderAbove(t *testing.T) {
	instance := serving(t, "9.9.9", theTree)
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "files", "mkdir", "/Reisen/2027/Belege")

	if got.code != exit.NotFound {
		t.Fatalf("want exit %d, got %d: %s", exit.NotFound, got.code, got.stderr)
	}
	if !strings.Contains(got.stderr, "--parents") {
		t.Fatalf("the sentence does not say what to do: %q", got.stderr)
	}

	// And nothing was made: a mkdir that half-succeeded would leave the tree
	// somewhere nobody asked for.
	for _, request := range instance.Requests {
		if request.Method == http.MethodPost {
			t.Fatalf("something was made anyway: %s", request.URL)
		}
	}
}

func TestFilesMoveReadsTheVersionItReplacesAndSendsNameAndPlaceTogether(t *testing.T) {
	var sent map[string]any
	var ifMatch string

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodPut {
			_ = json.NewDecoder(r.Body).Decode(&sent)
			ifMatch = r.Header.Get("If-Match")
			answering(theStoredFile)(w, r)
			return
		}
		theTree(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "files", "mv", "/plan.md", "/Reisen/der-plan.md")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	// One write carries both, because both are changes to the same row.
	if sent["name"] != "der-plan.md" || sent["folder"] != reisenID {
		t.Fatalf("what was sent is %v", sent)
	}

	// The version came from the listing pea already read, so nothing has to
	// hold a timestamp by hand.
	if ifMatch != `"2026-09-14T08:10:00.000000Z"` {
		t.Fatalf("the version sent is %q", ifMatch)
	}
}

func TestFilesMoveIntoAFolderKeepsTheNameItHas(t *testing.T) {
	var sent map[string]any

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodPut {
			_ = json.NewDecoder(r.Body).Decode(&sent)
			answering(theStoredFile)(w, r)
			return
		}
		theTree(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "files", "mv", "/plan.md", "/Reisen")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	// A destination that names an existing folder means "into it", which is
	// what mv means everywhere else.
	if sent["name"] != "plan.md" || sent["folder"] != reisenID {
		t.Fatalf("what was sent is %v", sent)
	}
}

func TestFilesRemoveSetsAsideAndSaysHowToGetItBack(t *testing.T) {
	var deleted string
	var ifMatch string

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodDelete {
			deleted = r.URL.Path
			ifMatch = r.Header.Get("If-Match")
			w.WriteHeader(http.StatusNoContent)
			return
		}
		theTree(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "files", "rm", "/plan.md")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if !strings.HasSuffix(deleted, "/api/files/0199f0c4-0000-7000-8000-00000000000f") {
		t.Fatalf("what was deleted is %q", deleted)
	}
	if ifMatch == "" {
		t.Fatalf("the delete was not guarded")
	}

	// `rm` here is not `scratchpad rm`, and the sentence has to be the one that
	// says so: nothing was destroyed.
	if !strings.Contains(got.stderr, "Trash") || !strings.Contains(got.stderr, "trash restore") {
		t.Fatalf("the sentence does not say it can come back: %q", got.stderr)
	}
}

func TestFilesRemoveOfAFolderUsesTheFolderAddress(t *testing.T) {
	var deleted string

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodDelete {
			deleted = r.URL.Path
			w.WriteHeader(http.StatusNoContent)
			return
		}
		theTree(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "files", "rm", "/Reisen")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if !strings.HasSuffix(deleted, "/api/files/folders/"+reisenID) {
		t.Fatalf("what was deleted is %q", deleted)
	}
	if !strings.Contains(got.stderr, "everything in it") {
		t.Fatalf("the sentence does not say what went with it: %q", got.stderr)
	}
}

func TestFilesJSONIsTheInstancesOwnDocument(t *testing.T) {
	instance := serving(t, "9.9.9", theTree)
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "--json", "files", "ls")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	var answered map[string]any
	if err := json.Unmarshal([]byte(got.stdout), &answered); err != nil {
		t.Fatalf("stdout is not JSON: %v (%q)", err, got.stdout)
	}
	if answered["max_file_bytes"] == nil || answered["used_bytes"] == nil {
		t.Fatalf("the listing lost what the instance answered: %v", answered)
	}
}

func TestFilesUsesTheIdWhenOneIsGivenRatherThanWalkingAPath(t *testing.T) {
	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if strings.Contains(r.URL.Path, "/api/files/"+bahnID) {
			answering(theStoredFile)(w, r)
			return
		}
		theTree(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "--json", "files", "get", bahnID)

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if !strings.Contains(got.stdout, "bahn.pdf") {
		t.Fatalf("the file was not read by its id: %q", got.stdout)
	}
}

func TestFilesSomethingThatIsNeitherAPathNorAnIdIsAUsageMistake(t *testing.T) {
	instance := serving(t, "9.9.9", theTree)
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "files", "rm", "/")

	if got.code != exit.Usage {
		t.Fatalf("want exit %d, got %d: %s", exit.Usage, got.code, got.stderr)
	}
}

func readAll(r *http.Request) ([]byte, error) {
	var buffer bytes.Buffer
	_, err := buffer.ReadFrom(r.Body)
	return buffer.Bytes(), err
}
