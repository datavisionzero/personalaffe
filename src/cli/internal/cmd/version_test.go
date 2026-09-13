package cmd_test

import (
	"encoding/json"
	"net/http"
	"strings"
	"testing"

	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/exit"
)

func TestVersionPrintsWhatTheInstanceActuallyAnswered(t *testing.T) {
	instance := serving(t, "9.9.9", answering(`{"version":"9.9.9"}`))
	env := environment(t, map[string]string{config.EnvURL: instance.URL})

	got := run(t, env, "version")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if !strings.Contains(got.stdout, "9.9.9") {
		t.Fatalf("the instance's version is not on stdout: %q", got.stdout)
	}
	if got.stderr != "" {
		t.Fatalf("nothing should have been said to a person: %q", got.stderr)
	}
	if asked := instance.Requests[0].URL.Path; asked != "/api/version" {
		t.Fatalf("pea asked %s", asked)
	}
}

func TestVersionInJsonIsJsonAndOnStdoutAlone(t *testing.T) {
	instance := serving(t, "9.9.9", answering(`{"version":"9.9.9"}`))
	env := environment(t, map[string]string{config.EnvURL: instance.URL})

	got := run(t, env, "version", "--json")

	var answered struct {
		Pea      string `json:"pea"`
		Instance string `json:"instance"`
	}
	if err := json.Unmarshal([]byte(got.stdout), &answered); err != nil {
		t.Fatalf("stdout is not JSON: %v\n%s", err, got.stdout)
	}
	if answered.Instance != "9.9.9" {
		t.Fatalf("want the instance's version, got %q", answered.Instance)
	}
	if answered.Pea == "" {
		t.Fatal("pea did not say what it is")
	}
	if got.stderr != "" {
		t.Fatalf("JSON output must not be shared with a sentence: %q", got.stderr)
	}
}

func TestVersionWithNoInstanceStillSaysWhatPeaIs(t *testing.T) {
	got := run(t, environment(t, nil), "version")

	// Not knowing which instance to ask is not a failure of this command: pea
	// knows what pea is.
	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d", got.code)
	}
	if !strings.Contains(got.stdout, "pea") {
		t.Fatalf("stdout says nothing about pea: %q", got.stdout)
	}
	if !strings.Contains(got.stderr, config.EnvURL) {
		t.Fatalf("stderr does not say what is missing: %q", got.stderr)
	}
}

func TestAnInstanceThatIsNotThereIsUnreachableAndNotABug(t *testing.T) {
	instance := serving(t, "", answering("{}"))
	address := instance.URL
	instance.Close()

	got := run(t, environment(t, map[string]string{config.EnvURL: address}), "version")

	if got.code != exit.Unreachable {
		t.Fatalf("want exit %d, got %d: %s", exit.Unreachable, got.code, got.stderr)
	}
	if !strings.Contains(got.stderr, "could not be reached") {
		t.Fatalf("stderr does not say what happened: %q", got.stderr)
	}
	if got.stdout != "" {
		t.Fatalf("a failure wrote to stdout: %q", got.stdout)
	}
}

func TestARefusalBecomesItsOwnExitCodeAndItsOwnSentence(t *testing.T) {
	cases := []struct {
		status   int
		document string
		want     int
		says     string
	}{
		{404, `{"type":"/problems/not-found","title":"Nothing at that address","detail":"No such endpoint."}`, exit.NotFound, "No such endpoint."},
		{401, `{"type":"/problems/unauthenticated","title":"No credential","detail":"The token is not known."}`, exit.Denied, "unauthenticated"},
		{400, `{"type":"/problems/validation","title":"A field is wrong","detail":"title: too long"}`, exit.Refused, "title: too long"},
		{412, `{"type":"/problems/stale","title":"It changed","detail":"Read it again."}`, exit.Stale, "stale"},
		{409, `{"type":"/problems/conflict","title":"Taken","detail":"That name is taken."}`, exit.Conflict, "conflict"},
		{500, `{"type":"/problems/internal","title":"Something went wrong on the server"}`, exit.Unexpected, "Something went wrong"},
	}

	for _, c := range cases {
		instance := serving(t, "", refusing(c.status, c.document))
		got := run(t, environment(t, map[string]string{config.EnvURL: instance.URL}), "version")

		if got.code != c.want {
			t.Errorf("status %d: want exit %d, got %d", c.status, c.want, got.code)
		}
		if !strings.Contains(got.stderr, c.says) {
			t.Errorf("status %d: stderr does not carry what the instance said: %q", c.status, got.stderr)
		}
		if got.stdout != "" {
			t.Errorf("status %d: a refusal wrote to stdout: %q", c.status, got.stdout)
		}
	}
}

func TestAnInstanceNewerThanPeaIsSkewAndSaysSo(t *testing.T) {
	instance := serving(t, "9.9.9", answering(`{"version":"9.9.9"}`))
	env := environment(t, map[string]string{config.EnvURL: instance.URL})

	// The build under test says 0.0.0-dev, which is deliberately never checked,
	// so the skew is proved against the rule rather than against this binary.
	// What is checked here is that a released pea would see it: the header is
	// read on every answer, including a refused one.
	got := run(t, env, "version")
	if got.code != exit.OK {
		t.Fatalf("a development build is not checked for skew: %d %s", got.code, got.stderr)
	}
}

func TestAPageOfHtmlWithATwoHundredIsNotASuccess(t *testing.T) {
	instance := serving(t, "", func(w http.ResponseWriter, _ *http.Request) {
		w.Header().Set("Content-Type", "text/html")
		_, _ = w.Write([]byte("<!doctype html><title>personalaffe</title>"))
	})

	got := run(t, environment(t, map[string]string{config.EnvURL: instance.URL}), "version")

	// The instance serves the web application from the same port. An endpoint an
	// older instance does not have answers index.html with a 200, and pea has to
	// say so rather than dereference JSON that was never there.
	if got.code != exit.Unexpected {
		t.Fatalf("want exit %d, got %d: %s", exit.Unexpected, got.code, got.stderr)
	}
	if !strings.Contains(got.stderr, "not JSON") {
		t.Fatalf("stderr does not say what arrived: %q", got.stderr)
	}
}
