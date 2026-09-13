// Package exit is the table of docs/cli.md: the code a script branches on,
// derived from the status and the problem type so that nothing has to be
// parsed.
package exit

import "github.com/datavisionzero/personalaffe/src/cli/internal/problem"

const (
	// OK is success: 2xx.
	OK = 0
	// Unexpected is a 500, a response pea cannot parse, or a bug in pea.
	Unexpected = 1
	// Usage is bad arguments, or PERSONALAFFE_URL or PERSONALAFFE_TOKEN unset
	// or malformed.
	Usage = 2
	// NotFound is 404.
	NotFound = 3
	// Refused is 400 validation and every 422.
	Refused = 4
	// Conflict is 409.
	Conflict = 5
	// Stale is 412: the object changed since it was read.
	Stale = 6
	// Denied is 401 and 403.
	Denied = 7
	// 8 is not given away. It is what "there is nothing" would be — a command
	// that looked and found no work — and no such command exists yet. Leaving
	// it free means no script has to relearn a number when one does.

	// Skew is a CLI too old or too new for the instance.
	Skew = 9
	// Unreachable is DNS, connection refused, timeout, TLS: the instance could
	// not be reached at all.
	Unreachable = 10
)

// FromResponse derives the code from a status and the problem document that
// came with it, if any.
func FromResponse(status int, p *problem.Problem) int {
	switch {
	case status >= 200 && status < 300:
		return OK
	case status == 401 || status == 403:
		return Denied
	case status == 404:
		return NotFound
	case status == 400 || status == 422:
		return Refused
	case status == 409:
		return Conflict
	case status == 412:
		return Stale
	default:
		return Unexpected
	}
}
