package exit_test

import (
	"testing"

	"github.com/datavisionzero/personalaffe/src/cli/internal/exit"
	"github.com/datavisionzero/personalaffe/src/cli/internal/problem"
)

func TestEveryAnswerHasACodeAScriptCanBranchOn(t *testing.T) {
	cases := []struct {
		status int
		body   string
		want   int
	}{
		{200, "", exit.OK},
		{204, "", exit.OK},
		{400, `{"type":"/problems/validation"}`, exit.Refused},
		{401, `{"type":"/problems/unauthenticated"}`, exit.Denied},
		{403, `{"type":"/problems/forbidden"}`, exit.Denied},
		{404, `{"type":"/problems/not-found"}`, exit.NotFound},
		{409, `{"type":"/problems/conflict"}`, exit.Conflict},
		{412, `{"type":"/problems/stale"}`, exit.Stale},
		{422, `{"type":"/problems/transition"}`, exit.Refused},
		{500, `{"type":"/problems/internal"}`, exit.Unexpected},
		{418, "", exit.Unexpected},
	}

	for _, c := range cases {
		got := exit.FromResponse(c.status, problem.Parse([]byte(c.body)))
		if got != c.want {
			t.Errorf("status %d: want exit %d, got %d", c.status, c.want, got)
		}
	}
}

func TestTheCodesAreDistinctSoAScriptCanTellThemApart(t *testing.T) {
	codes := map[int]string{
		exit.OK:          "OK",
		exit.Unexpected:  "Unexpected",
		exit.Usage:       "Usage",
		exit.NotFound:    "NotFound",
		exit.Refused:     "Refused",
		exit.Conflict:    "Conflict",
		exit.Stale:       "Stale",
		exit.Denied:      "Denied",
		exit.Skew:        "Skew",
		exit.Unreachable: "Unreachable",
	}

	if len(codes) != 10 {
		t.Fatalf("two codes share a number: %v", codes)
	}

	// 8 is deliberately not given away, so that "there is nothing" can have it
	// when a command that looks for work exists.
	if _, taken := codes[8]; taken {
		t.Fatal("8 was given away")
	}
}
