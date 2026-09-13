package cmd_test

import (
	"strings"
	"testing"

	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/exit"
)

func TestAMistakeInTheArgumentsIsExitTwoAndNotABug(t *testing.T) {
	for _, args := range [][]string{
		{"nonsense"},
		{"version", "extra"},
		{"--nonsense"},
		{"--url"},
	} {
		got := run(t, environment(t, nil), args...)

		if got.code != exit.Usage {
			t.Errorf("%v: want exit %d, got %d (%s)", args, exit.Usage, got.code, got.stderr)
		}
		if got.stderr == "" {
			t.Errorf("%v: nothing was said about what was wrong", args)
		}
	}
}

func TestHelpIsAnAnswerAndGoesToStdout(t *testing.T) {
	got := run(t, environment(t, nil), "--help")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d", got.code)
	}
	if !strings.Contains(got.stdout, "version") || !strings.Contains(got.stdout, "status") {
		t.Fatalf("help does not list the verbs: %q", got.stdout)
	}
}

func TestPlainHttpToAPublicHostIsRefusedBeforeARequestIsMade(t *testing.T) {
	got := run(t, environment(t, map[string]string{config.EnvURL: "http://workspace.example.com"}), "version")

	if got.code != exit.Usage {
		t.Fatalf("want exit %d, got %d", exit.Usage, got.code)
	}
	if !strings.Contains(got.stderr, config.EnvInsecureHTTP) {
		t.Fatalf("the refusal does not say how to mean it: %q", got.stderr)
	}
}

func TestNoCommandReadsStdinUnlessAFlagSaidSo(t *testing.T) {
	instance := serving(t, "9.9.9", answering(`{"version":"9.9.9"}`))
	env := environment(t, map[string]string{config.EnvURL: instance.URL})

	// A stdin nothing ever writes to. pea reads stdin only where a flag says so,
	// and never prompts; a command that waited on this one would never return
	// and this test would time out rather than pass.
	for _, verb := range []string{"version", "status", "--help"} {
		if got := runWith(t, blocking{}, env, verb); got.stdout == "" {
			t.Errorf("%s answered nothing", verb)
		}
	}
}
