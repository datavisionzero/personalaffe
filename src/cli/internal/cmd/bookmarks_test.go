package cmd_test

import (
	"encoding/json"
	"fmt"
	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/exit"
	"net/http"
	"strings"
	"testing"
)

const bookmarkID = "0199f0c4-0000-7000-8000-000000000001"
const bookmarkJSON = `{"id":"0199f0c4-0000-7000-8000-000000000001","title":"Docs","url":"https://example.com/docs","description":"Keep me","folder":null,"private":false,"favorite":false,"favorite_position":null,"created_at":"2026-09-20T08:00:00Z","updated_at":"2026-09-20T08:00:00Z"}`

func TestBookmarkPrivateContextIsExplicitAndDoesNotPersistBetweenInvocations(t *testing.T) {
	instance := serving(t, "9.9.9", answering(`{"items":[`+bookmarkJSON+`],"next_offset":null}`))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})
	for _, args := range [][]string{{"bookmarks", "ls", "--include-private", "--json"}, {"bookmarks", "search", "doc", "--folder", "unsorted"}, {"trash", "list", "--include-private"}, {"search", "doc", "--include-private"}} {
		got := run(t, env, args...)
		if got.code != exit.OK {
			t.Fatalf("%v: %d %s", args, got.code, got.stderr)
		}
	}
	if _, present := instance.Requests[0].URL.Query()["tag"]; present {
		t.Fatal("empty tags must omit the query parameter")
	}
	for i, want := range []string{"true", "", "true", "true"} {
		if instance.Requests[i].Header.Get("Personalaffe-Private") != want {
			t.Fatalf("request %d private context = %q", i, instance.Requests[i].Header.Get("Personalaffe-Private"))
		}
	}
	if instance.Requests[1].URL.Query().Get("unsorted") != "true" || instance.Requests[1].URL.Query().Get("q") != "doc" {
		t.Fatal("missing search filters")
	}
	for _, r := range instance.Requests {
		if r.Method != "GET" {
			t.Fatal("reading recorded an opening")
		}
	}
}
func TestBookmarkEditPreservesUnspecifiedFieldsAndHoldsReadVersion(t *testing.T) {
	var body map[string]any
	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		w.Header().Set("ETag", `"2026-09-20T08:00:00Z"`)
		if r.Method == "PUT" {
			if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
				t.Error(err)
			}
		}
		fmt.Fprint(w, bookmarkJSON)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})
	got := run(t, env, "bookmarks", "edit", bookmarkID, "--title", "New title", "--include-private")
	if got.code != exit.OK {
		t.Fatal(got.stderr)
	}
	if body["title"] != "New title" || body["description"] != "Keep me" || body["url"] != "https://example.com/docs" {
		t.Fatalf("unexpected body: %v", body)
	}
	if len(instance.Requests) != 2 || instance.Requests[1].Header.Get("If-Match") != `"2026-09-20T08:00:00Z"` {
		t.Fatal("write lost read version")
	}
	for _, r := range instance.Requests {
		if r.Header.Get("Personalaffe-Private") != "true" {
			t.Fatal("private read/write mismatch")
		}
	}
}
func TestBookmarkExplicitVersionAndConflictNeverRetry(t *testing.T) {
	instance := serving(t, "9.9.9", refusing(http.StatusPreconditionFailed, `{"type":"/problems/stale","title":"Changed","status":412}`))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})
	got := run(t, env, "bookmarks", "rm", bookmarkID, "--if-match", `"old"`)
	if got.code != exit.Stale {
		t.Fatalf("want stale, got %d: %s", got.code, got.stderr)
	}
	if len(instance.Requests) != 1 || instance.Requests[0].Method != "DELETE" || instance.Requests[0].Header.Get("If-Match") != `"old"` {
		t.Fatal("explicit version caused unexpected request")
	}
}
func TestBookmarkCreationSuggestsDomainAndFolderCreationCarriesPrivateSetting(t *testing.T) {
	var bodies []map[string]any
	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		var body map[string]any
		if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
			t.Error(err)
		}
		bodies = append(bodies, body)
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(http.StatusCreated)
		if strings.HasSuffix(r.URL.Path, "/folders") {
			fmt.Fprintf(w, `{"id":%q,"name":"Secret","parent":null,"private":true,"effective_private":true}`, bookmarkID)
		} else {
			fmt.Fprint(w, bookmarkJSON)
		}
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})
	for _, args := range [][]string{{"bookmarks", "add", "https://example.com/docs"}, {"bookmarks", "folders", "add", "Secret", "--private", "--include-private"}} {
		if got := run(t, env, args...); got.code != exit.OK {
			t.Fatal(got.stderr)
		}
	}
	if bodies[0]["title"] != "example.com" || bodies[1]["private"] != true {
		t.Fatalf("wrong creation bodies: %v", bodies)
	}
}
func TestBookmarkFavoriteCarriesPredecessorWithoutOpening(t *testing.T) {
	var body map[string]any
	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
			t.Error(err)
		}
		answering(bookmarkJSON)(w, r)
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})
	after := "0199f0c4-0000-7000-8000-000000000002"
	got := run(t, env, "bookmarks", "favorite", bookmarkID, "--after", after, "--if-match", `"current"`)
	if got.code != exit.OK {
		t.Fatal(got.stderr)
	}
	if body["after"] != after || body["favorite"] != true || len(instance.Requests) != 1 {
		t.Fatal("favorite did not carry intended order")
	}
}

func TestBookmarkImportRequiresReviewedConfirmationAndExportHasSeparatePrivateOptIn(t *testing.T) {
	var paths []string
	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		paths = append(paths, r.URL.Path)
		switch r.URL.Path {
		case "/api/bookmarks/import/preview":
			answering(`{"valid_bookmarks":1,"folders":0,"new_bookmarks":1,"new_folders":0,"skipped_duplicates":0,"rejected":[],"preview_hash":"reviewed"}`)(w, r)
		case "/api/bookmarks/import":
			var body map[string]any
			if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
				t.Error(err)
			}
			if body["preview_hash"] != "reviewed" {
				t.Error("missing reviewed hash")
			}
			answering(`{"imported_bookmarks":1,"created_folders":0,"skipped_duplicates":0,"rejected":[]}`)(w, r)
		case "/api/bookmarks/export":
			if r.Header.Get("Personalaffe-Private") != "true" || r.URL.Query().Get("include_private") != "false" {
				t.Error("private context alone opted into private export")
			}
			answering(`{"html":"<DL></DL>","bookmarks":0,"folders":0,"warning":"Unencrypted"}`)(w, r)
		default:
			t.Error("unexpected route", r.URL.Path)
		}
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})
	if got := runWith(t, strings.NewReader("<A HREF='https://example.com'>Example</A>"), env, "bookmarks", "import", "--file", "-", "--json"); got.code != exit.OK || !strings.Contains(got.stdout, "reviewed") {
		t.Fatal(got)
	}
	if len(paths) != 1 || paths[0] != "/api/bookmarks/import/preview" {
		t.Fatal("preview wrote data")
	}
	if got := run(t, env, "bookmarks", "import", "--file", "-", "--confirm"); got.code != exit.Usage {
		t.Fatal("confirmation accepted without reviewed plan")
	}
	if got := runWith(t, strings.NewReader("<A HREF='https://example.com'>Example</A>"), env, "bookmarks", "import", "--file", "-", "--confirm", "--preview-hash", "reviewed"); got.code != exit.OK {
		t.Fatal(got)
	}
	if got := run(t, env, "bookmarks", "export", "--include-private"); got.code != exit.OK || got.stdout != "<DL></DL>" {
		t.Fatal(got)
	}
	if got := run(t, env, "bookmarks", "export", "--export-private"); got.code != exit.Usage {
		t.Fatal("private export accepted without context")
	}
}

func TestBookmarkTagsUseRepeatedFiltersAndExplicitReplacement(t *testing.T) {
	var written map[string]any
	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("ETag", `"current"`)
		if r.Method == "PUT" {
			if err := json.NewDecoder(r.Body).Decode(&written); err != nil {
				t.Error(err)
			}
		}
		if r.URL.Path == "/api/bookmarks" {
			answering(`{"items":[],"next_offset":null}`)(w, r)
		} else {
			answering(bookmarkJSON)(w, r)
		}
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})
	if got := run(t, env, "bookmarks", "ls", "--tag", "work", "--tag", "research"); got.code != exit.OK {
		t.Fatal(got)
	}
	if values := instance.Requests[0].URL.Query()["tag"]; len(values) != 2 || values[0] != "work" || values[1] != "research" {
		t.Fatalf("wrong all-tag filter: %v", values)
	}
	if got := run(t, env, "bookmarks", "edit", bookmarkID, "--tag", "work", "--tag", "research"); got.code != exit.OK {
		t.Fatal(got)
	}
	if len(written["tags"].([]any)) != 2 {
		t.Fatalf("tags not replaced: %v", written)
	}
	if got := run(t, env, "bookmarks", "edit", bookmarkID, "--clear-tags"); got.code != exit.OK {
		t.Fatal(got)
	}
	if len(written["tags"].([]any)) != 0 {
		t.Fatalf("tags not cleared: %v", written)
	}
}

func TestBookmarkReadingFiltersAndUndoTimestampAreExplicitAndGuarded(t *testing.T) {
	var written map[string]any
	instance := serving(t, "9.9.9", func(w http.ResponseWriter, r *http.Request) {
		if r.Method == "PUT" {
			if err := json.NewDecoder(r.Body).Decode(&written); err != nil {
				t.Error(err)
			}
			answering(bookmarkJSON)(w, r)
		} else {
			answering(`{"items":[],"next_offset":null}`)(w, r)
		}
	})
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})
	if got := run(t, env, "bookmarks", "reading", "--tag", "research"); got.code != exit.OK {
		t.Fatal(got)
	}
	if instance.Requests[0].URL.Query().Get("read_later") != "true" || instance.Requests[0].URL.Query().Get("sort") != "reading" {
		t.Fatal("reading list lost membership or ordering")
	}
	if got := run(t, env, "bookmarks", "read", bookmarkID, "--if-match", `"current"`); got.code != exit.OK {
		t.Fatal(got)
	}
	if written["read_later"] != false {
		t.Fatal("not marked read")
	}
	if got := run(t, env, "bookmarks", "read-later", bookmarkID, "--if-match", `"next"`, "--queued-at", "2026-09-20T08:00:00Z"); got.code != exit.OK {
		t.Fatal(got)
	}
	if written["read_later"] != true || written["queued_at"] != "2026-09-20T08:00:00Z" {
		t.Fatal("undo lost queue timestamp")
	}
	if instance.Requests[2].Header.Get("If-Match") != `"next"` {
		t.Fatal("undo lost version")
	}
}
