// Package render is how pea prints: as JSON for something reading it, and as
// lines for somebody reading it. Both go to stdout; sentences about what
// happened go to stderr, which is the separation docs/cli.md promises.
package render

import (
	"encoding/json"
	"fmt"
	"io"
)

// JSON writes one object, indented and with a trailing newline, so that a
// terminal and a `jq` both get something reasonable. HTML escaping is off: the
// values are a person's own text, not a web page, and `&` should stay `&`.
func JSON(out io.Writer, value any) error {
	encoder := json.NewEncoder(out)
	encoder.SetIndent("", "  ")
	encoder.SetEscapeHTML(false)

	return encoder.Encode(value)
}

// Field writes one `name  value` line, aligned on a width the caller chooses,
// which is how every human-readable answer in pea is shaped.
func Field(out io.Writer, width int, name, value string) {
	fmt.Fprintf(out, "%-*s  %s\n", width, name, value)
}
