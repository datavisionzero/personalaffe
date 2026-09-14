package cmd_test

import (
	"encoding/json"
	"net/http"
	"strings"
	"testing"

	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/exit"
)

const einkaufID = "0199f0c7-0000-7000-8000-00000000000a"

const milchID = "0199f0c7-0000-7000-8000-00000000000b"

const brotID = "0199f0c7-0000-7000-8000-00000000000c"

const theLists = `{"items":[
 {"id":"0199f0c7-0000-7000-8000-00000000000a","name":"Einkauf","open":1,"all":2,
  "created_at":"2026-09-14T08:00:00.000000Z","updated_at":"2026-09-14T08:00:00.000000Z"}]}`

const theTasks = `{"items":[
 {"id":"0199f0c7-0000-7000-8000-00000000000b","list":"0199f0c7-0000-7000-8000-00000000000a",
  "title":"Milch holen","description":"am Markt","due_on":"2026-09-14",
  "completed":false,"completed_at":null,"after":null,
  "created_at":"2026-09-14T08:10:00.000000Z","updated_at":"2026-09-14T08:10:00.000000Z"},
 {"id":"0199f0c7-0000-7000-8000-00000000000c","list":"0199f0c7-0000-7000-8000-00000000000a",
  "title":"Brot holen","description":"","due_on":null,
  "completed":true,"completed_at":"2026-09-14T09:00:00.000000Z",
  "after":"0199f0c7-0000-7000-8000-00000000000b",
  "created_at":"2026-09-14T08:20:00.000000Z","updated_at":"2026-09-14T09:00:00.000000Z"}]}`

const theMilkTask = `{"id":"0199f0c7-0000-7000-8000-00000000000b",
 "list":"0199f0c7-0000-7000-8000-00000000000a","title":"Milch holen","description":"am Markt",
 "due_on":"2026-09-14","completed":false,"completed_at":null,"after":null,
 "created_at":"2026-09-14T08:10:00.000000Z","updated_at":"2026-09-14T08:10:00.000000Z"}`

// theBoard answers the three reads every verb makes, and nothing else.
func theBoard(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Content-Type", "application/json")

	switch {
	case strings.HasSuffix(r.URL.Path, "/tasks/lists"):
		_, _ = w.Write([]byte(theLists))
	case strings.HasSuffix(r.URL.Path, "/tasks"):
		_, _ = w.Write([]byte(theTasks))
	default:
		_, _ = w.Write([]byte(theMilkTask))
	}
}

func TestTasksListsPrintsOneLinePerList(t *testing.T) {
	instance := serving(t, "9.9.9", theBoard)
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "tasks", "lists")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	line := strings.Split(strings.TrimSpace(got.stdout), "\t")
	if len(line) != 4 || line[1] != "1" || line[2] != "2" || line[3] != "Einkauf" {
		t.Fatalf("the list's line is %q", got.stdout)
	}
}

func TestTasksListShowsOpenBeforeCompletedAndTheDayAsItWasWritten(t *testing.T) {
	instance := serving(t, "9.9.9", theBoard)
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "tasks", "ls", "Einkauf")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	lines := strings.Split(strings.TrimSpace(got.stdout), "\n")
	if len(lines) != 2 {
		t.Fatalf("want two lines, got %q", got.stdout)
	}

	// Open first and completed after: two different things to be looking at.
	if !strings.Contains(lines[0], "open") || !strings.Contains(lines[1], "done") {
		t.Fatalf("the order is %q", got.stdout)
	}

	// The day as the instance wrote it. A date that went through a timezone on
	// its way to a terminal is a date that can be off by one.
	if !strings.Contains(lines[0], "2026-09-14") {
		t.Fatalf("the due date is not the day it was: %q", lines[0])
	}
	if !strings.Contains(lines[1], "\t-\t") {
		t.Fatalf("a task with no date should say so: %q", lines[1])
	}
}

func TestTasksListResolvesANameAgainstOneReadAndSendsIds(t *testing.T) {
	instance := serving(t, "9.9.9", theBoard)
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	if got := run(t, env, "tasks", "ls", "einkauf"); got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	for _, request := range instance.Requests {
		if strings.Contains(request.URL.Path, "Einkauf") || strings.Contains(request.URL.Path, "einkauf") {
			t.Fatalf("a name went to the instance: %s", request.URL)
		}
	}
}

func TestTasksAListThatIsNotThereIsExitThree(t *testing.T) {
	instance := serving(t, "9.9.9", theBoard)
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "tasks", "ls", "Arbeit")

	if got.code != exit.NotFound {
		t.Fatalf("want exit %d, got %d: %s", exit.NotFound, got.code, got.stderr)
	}
	if !strings.Contains(got.stderr, "Arbeit") {
		t.Fatalf("the sentence does not name it: %q", got.stderr)
	}
}

func TestTasksAddSendsTheTitleAndTheDayAndPrintsTheId(t *testing.T) {
	var sent map[string]any
	var where string

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodPost {
			_ = json.NewDecoder(r.Body).Decode(&sent)
			where = r.URL.Path
			w.Header().Set("Content-Type", "application/json")
			w.WriteHeader(http.StatusCreated)
			_, _ = w.Write([]byte(theMilkTask))
			return
		}
		theBoard(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "tasks", "add", "Einkauf", "Milch holen", "--due", "2026-09-14")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if sent["title"] != "Milch holen" || sent["due_on"] != "2026-09-14" {
		t.Fatalf("what was sent is %v", sent)
	}
	if !strings.HasSuffix(where, "/api/tasks/lists/"+einkaufID+"/tasks") {
		t.Fatalf("it went to %q", where)
	}
	if strings.TrimSpace(got.stdout) != milchID {
		t.Fatalf("stdout is not the id: %q", got.stdout)
	}
}

func TestTasksAddRefusesSomethingThatIsNotADay(t *testing.T) {
	instance := serving(t, "9.9.9", theBoard)
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "tasks", "add", "Einkauf", "Milch holen", "--due", "morgen")

	if got.code != exit.Usage {
		t.Fatalf("want exit %d, got %d: %s", exit.Usage, got.code, got.stderr)
	}
	if !strings.Contains(got.stderr, "2026-09-14") {
		t.Fatalf("the sentence does not say what a day looks like: %q", got.stderr)
	}
}

func TestTasksShowWritesTheDescriptionToStdoutAndTheRestToStderr(t *testing.T) {
	instance := serving(t, "9.9.9", theBoard)
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "tasks", "show", milchID)

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if got.stdout != "am Markt" {
		t.Fatalf("stdout is %q", got.stdout)
	}
	if !strings.Contains(got.stderr, "Milch holen") {
		t.Fatalf("stderr does not say what it is: %q", got.stderr)
	}
}

func TestTasksEditCarriesForwardWhateverItWasNotGiven(t *testing.T) {
	var sent map[string]any
	var ifMatch string

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodPut {
			_ = json.NewDecoder(r.Body).Decode(&sent)
			ifMatch = r.Header.Get("If-Match")
			answering(theMilkTask)(w, r)
			return
		}
		theBoard(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "tasks", "edit", milchID, "--title", "Milch und Brot")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	// One write carries the whole task, and the five it was not given are what
	// the instance already had.
	if sent["title"] != "Milch und Brot" || sent["description"] != "am Markt" ||
		sent["due_on"] != "2026-09-14" || sent["completed"] != false ||
		sent["list"] != einkaufID || sent["after"] != nil {
		t.Fatalf("what was sent is %v", sent)
	}
	if ifMatch != `"2026-09-14T08:10:00.000000Z"` {
		t.Fatalf("the version sent is %q", ifMatch)
	}
}

func TestTasksEditCanTakeTheDateAway(t *testing.T) {
	var sent map[string]any

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodPut {
			_ = json.NewDecoder(r.Body).Decode(&sent)
			answering(theMilkTask)(w, r)
			return
		}
		theBoard(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "tasks", "edit", milchID, "--due", "none")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	// An empty string could not say this: not giving the flag at all already
	// means "leave it alone".
	if _, there := sent["due_on"]; !there || sent["due_on"] != nil {
		t.Fatalf("the date was not taken away: %v", sent)
	}
}

func TestTasksDoneAndUndoneAreTheSameWriteWithOneFieldChanged(t *testing.T) {
	var sent map[string]any

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodPut {
			_ = json.NewDecoder(r.Body).Decode(&sent)
			answering(theMilkTask)(w, r)
			return
		}
		theBoard(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	if got := run(t, env, "tasks", "done", milchID); got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if sent["completed"] != true || sent["title"] != "Milch holen" {
		t.Fatalf("what was sent is %v", sent)
	}

	if got := run(t, env, "tasks", "undone", milchID); got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if sent["completed"] != false {
		t.Fatalf("what was sent is %v", sent)
	}
}

func TestTasksMoveSendsANeighbourAndNotANumber(t *testing.T) {
	var sent map[string]any

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodPut {
			_ = json.NewDecoder(r.Body).Decode(&sent)
			answering(theMilkTask)(w, r)
			return
		}
		theBoard(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	if got := run(t, env, "tasks", "mv", milchID, "--after", brotID); got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if sent["after"] != brotID {
		t.Fatalf("what was sent is %v", sent)
	}

	if got := run(t, env, "tasks", "mv", milchID, "--top"); got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if sent["after"] != nil {
		t.Fatalf("the top of the list is nothing, not %v", sent["after"])
	}
}

func TestTasksMoveWithNeitherPlaceIsAUsageMistake(t *testing.T) {
	instance := serving(t, "9.9.9", theBoard)
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "tasks", "mv", milchID)

	if got.code != exit.Usage {
		t.Fatalf("want exit %d, got %d: %s", exit.Usage, got.code, got.stderr)
	}
}

func TestTasksEditOfAStaleReadIsExitSix(t *testing.T) {
	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodPut {
			refusing(http.StatusPreconditionFailed, `{"type":"/problems/stale",
 "title":"The object has changed since it was read","status":412,
 "detail":"The task has changed since it was read. Read it again."}`)(w, r)
			return
		}
		theBoard(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "tasks", "done", milchID)

	if got.code != exit.Stale {
		t.Fatalf("want exit %d, got %d: %s", exit.Stale, got.code, got.stderr)
	}
}

func TestTasksRemoveSaysItIsInTheTrashAndHowToGetItBack(t *testing.T) {
	var deleted string

	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == http.MethodDelete {
			deleted = r.URL.Path
			w.WriteHeader(http.StatusNoContent)
			return
		}
		theBoard(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "tasks", "rm", milchID)

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if !strings.HasSuffix(deleted, "/api/tasks/"+milchID) {
		t.Fatalf("what was deleted is %q", deleted)
	}
	if !strings.Contains(got.stderr, "trash restore tasks") {
		t.Fatalf("the sentence does not say how to get it back: %q", got.stderr)
	}
}

func TestTasksJSONIsTheInstancesOwnDocument(t *testing.T) {
	instance := serving(t, "9.9.9", theBoard)
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "--json", "tasks", "ls", "Einkauf")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	var answered map[string]any
	if err := json.Unmarshal([]byte(got.stdout), &answered); err != nil {
		t.Fatalf("stdout is not JSON: %v (%q)", err, got.stdout)
	}
	if len(answered["items"].([]any)) != 2 {
		t.Fatalf("the listing lost what the instance answered: %v", answered)
	}
}

func TestTasksAnEmptyListWritesNothingToStdout(t *testing.T) {
	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		if strings.HasSuffix(r.URL.Path, "/tasks/lists") {
			_, _ = w.Write([]byte(theLists))
			return
		}
		_, _ = w.Write([]byte(`{"items":[]}`))
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "tasks", "ls", "Einkauf")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if got.stdout != "" {
		t.Fatalf("stdout is not empty: %q", got.stdout)
	}
	if !strings.Contains(got.stderr, "empty") {
		t.Fatalf("stderr says nothing: %q", got.stderr)
	}
}
