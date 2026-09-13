// Package client wraps the generated client with what every request carries and
// what every response is checked for: the bearer token, the User-Agent naming
// this build, and the version of the instance against pea's own
// (docs/api.md, docs/cli.md).
package client

import (
	"context"
	"errors"
	"fmt"
	"mime"
	"net"
	"net/http"
	"net/url"
	"os"
	"runtime"
	"strings"
	"time"

	"github.com/datavisionzero/personalaffe/src/cli/internal/api"
	"github.com/datavisionzero/personalaffe/src/cli/internal/exit"
	"github.com/datavisionzero/personalaffe/src/cli/internal/problem"
	"github.com/datavisionzero/personalaffe/src/cli/internal/version"
)

// Failure is an answer pea turns into an exit code: what to print to stderr,
// and which code.
type Failure struct {
	Code    int
	Message string
	Problem *problem.Problem
}

func (f *Failure) Error() string { return f.Message }

// VersionHeader is what every response carries (docs/api.md).
const VersionHeader = "Personalaffe-Version"

// Client is one invocation's client.
type Client struct {
	*api.ClientWithResponses
}

// New builds the client for one instance. An empty token sends no header rather
// than an empty one, which would be a credential the instance has to refuse:
// the three operations of the foundation take none.
func New(address, token string, httpClient *http.Client) (*Client, error) {
	generated, err := api.NewClientWithResponses(
		address,
		api.WithHTTPClient(httpClient),
		api.WithRequestEditorFn(func(_ context.Context, req *http.Request) error {
			if token != "" {
				req.Header.Set("Authorization", "Bearer "+token)
			}
			req.Header.Set("User-Agent", UserAgent())
			return nil
		}))
	if err != nil {
		return nil, err
	}

	return &Client{ClientWithResponses: generated}, nil
}

// UserAgent is `pea/<version> (<os>/<arch>)`.
func UserAgent() string {
	return fmt.Sprintf("pea/%s (%s/%s)", version.Version, runtime.GOOS, runtime.GOARCH)
}

// Default is the HTTP client pea uses: a timeout, because an agent must never
// hang on a request, and nothing else a command has to remember to set.
func Default() *http.Client {
	return &http.Client{Timeout: 30 * time.Second}
}

// Check turns a response into a Failure when it is one: the version skew first,
// because a skewed instance's refusal may be about a shape pea does not know;
// then a success that is not the JSON it claims to be; then the problem
// document by the table of docs/cli.md.
func Check(resp *http.Response, body []byte) error {
	if resp == nil {
		return &Failure{Code: exit.Unexpected, Message: "no response"}
	}

	if ok, reason := version.Compatible(version.Version, resp.Header.Get(VersionHeader)); !ok {
		return &Failure{Code: exit.Skew, Message: reason}
	}

	if resp.StatusCode >= 200 && resp.StatusCode < 300 {
		return notJSON(resp, body)
	}

	p := problem.Parse(body)
	message := p.Message()
	if message == "" {
		message = fmt.Sprintf("the instance answered %s", resp.Status)
	}

	return &Failure{Code: exit.FromResponse(resp.StatusCode, p), Message: message, Problem: p}
}

// notJSON catches a success that is not the shape the contract promises, and it
// is the one answer pea cannot tell from a good one by its status alone.
//
// The instance serves the web application from the same port and falls back to
// index.html for every path outside `/api` (docs/codebase.md). An address the
// prefix does not cover answers a page of HTML with a 200 — a success, to every
// check there was — and a verb would then dereference JSON the generated client
// never filled in and die on a nil pointer instead of saying what was wrong.
func notJSON(resp *http.Response, body []byte) error {
	// A 204 and anything else that answers with nothing is a fine success.
	if len(body) == 0 {
		return nil
	}

	kind, _, err := mime.ParseMediaType(resp.Header.Get("Content-Type"))
	if err == nil && (kind == "application/json" || strings.HasSuffix(kind, "+json")) {
		return nil
	}

	where := ""
	if resp.Request != nil && resp.Request.URL != nil {
		where = " for " + resp.Request.URL.Path
	}

	return &Failure{
		Code: exit.Unexpected,
		Message: fmt.Sprintf(
			"the instance answered %s%s with %s, not JSON — most likely it does not have this endpoint yet; "+
				"`pea version` says what it is",
			resp.Status, where, described(kind)),
	}
}

func described(kind string) string {
	if kind == "" {
		return "something else"
	}
	return kind
}

// Transport turns an error from the HTTP client into the Failure it is: the
// instance could not be reached, exit 10.
func Transport(err error) error {
	if err == nil {
		return nil
	}

	var failure *Failure
	if errors.As(err, &failure) {
		return failure
	}

	var urlErr *url.Error
	var netErr net.Error
	if errors.As(err, &urlErr) || errors.As(err, &netErr) ||
		errors.Is(err, context.DeadlineExceeded) || errors.Is(err, os.ErrDeadlineExceeded) {
		return &Failure{
			Code:    exit.Unreachable,
			Message: fmt.Sprintf("the instance could not be reached: %v", err),
		}
	}

	return &Failure{Code: exit.Unexpected, Message: err.Error()}
}
