package cmd_test

import (
	"bytes"
	"context"
	"io"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"testing"

	"github.com/datavisionzero/personalaffe/src/cli/internal/client"
	"github.com/datavisionzero/personalaffe/src/cli/internal/cmd"
	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/keychain"
)

// held is a keychain in memory. No test ever touches the keychain of the
// machine it runs on, and a machine with none is `held(nil)` — which is what
// CI and an agent's container actually are.
type held map[string]string

func (h held) Load(instance string) (string, error) {
	if h == nil {
		return "", keychain.ErrNoKeychain
	}
	if token, ok := h[instance]; ok {
		return token, nil
	}
	return "", keychain.ErrNotFound
}

func (h held) Save(instance, token string) error {
	if h == nil {
		return keychain.ErrNoKeychain
	}
	h[instance] = token
	return nil
}

func (h held) Delete(instance string) error {
	if h == nil {
		return keychain.ErrNoKeychain
	}
	delete(h, instance)
	return nil
}

func (held) Where() string { return "a keychain in a test" }

// run executes pea the way main does, with everything it reads supplied by the
// test: no command ever reads this machine's environment, its home directory or
// its files.
type result struct {
	code   int
	stdout string
	stderr string
}

type instance struct {
	*httptest.Server
	// Requests is what the instance was actually sent, so that a test can say
	// which credential travelled and which did not.
	Requests []*http.Request
}

// serving stands an instance up that answers `answer` for every request, with
// the version header every answer of a real one carries.
func serving(t *testing.T, version string, answer http.HandlerFunc) *instance {
	t.Helper()

	stood := &instance{}
	stood.Server = httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		stood.Requests = append(stood.Requests, r.Clone(r.Context()))
		if version != "" {
			w.Header().Set(client.VersionHeader, version)
		}
		answer(w, r)
	}))
	t.Cleanup(stood.Close)

	return stood
}

func answering(body string) http.HandlerFunc {
	return func(w http.ResponseWriter, _ *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(body))
	}
}

func refusing(status int, document string) http.HandlerFunc {
	return func(w http.ResponseWriter, _ *http.Request) {
		w.Header().Set("Content-Type", "application/problem+json")
		w.WriteHeader(status)
		_, _ = w.Write([]byte(document))
	}
}

// environment is what the ladders read. The configuration file is always
// pointed at a temporary path, so that whoever runs the tests keeps their own.
func environment(t *testing.T, values map[string]string) func(string) string {
	t.Helper()

	all := map[string]string{config.EnvConfig: filepath.Join(t.TempDir(), "config.json")}
	for name, value := range values {
		all[name] = value
	}

	return func(name string) string { return all[name] }
}

func run(t *testing.T, getenv func(string) string, args ...string) result {
	t.Helper()
	return runWith(t, bytes.NewReader(nil), getenv, args...)
}

func runWith(t *testing.T, stdin io.Reader, getenv func(string) string, args ...string) result {
	t.Helper()
	return runHolding(t, held{}, stdin, getenv, args...)
}

func runHolding(
	t *testing.T, store keychain.Keychain, stdin io.Reader, getenv func(string) string, args ...string,
) result {
	t.Helper()

	var stdout, stderr bytes.Buffer
	code := cmd.Run(context.Background(), args, cmd.Env{
		Getenv:   getenv,
		Stdin:    stdin,
		Stdout:   &stdout,
		Stderr:   &stderr,
		Keychain: store,
	})

	return result{code: code, stdout: stdout.String(), stderr: stderr.String()}
}

// readFile is what a test reads the configuration back with, so that no test
// has to reach for os itself.
func readFile(path string) (string, error) {
	content, err := os.ReadFile(path)
	return string(content), err
}

// blocking is a stdin nothing ever writes to. A command that read it without
// being told to would never return, and the test that used it would time out
// rather than pass.
type blocking struct{}

func (blocking) Read([]byte) (int, error) {
	select {}
}
