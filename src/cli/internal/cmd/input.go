package cmd

import (
	"fmt"
	"io"
	"os"
	"strings"

	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
)

// readText reads a piece of a person's text from a file, or from stdin for `-`
// — never from an editor. An empty path means the flag was not given.
//
// It is here before the commands that need it because every one of them will:
// a scratchpad entry, a knowledge page, a task's description. What an agent
// pipes in is bytes, and the two things that must survive the trip are the ones
// that break first — text that is not ASCII, and text with newlines in it.
func readText(stdin io.Reader, path string) (*string, error) {
	if path == "" {
		return nil, nil
	}

	data, err := read(stdin, path)
	if err != nil {
		return nil, err
	}

	// A trailing newline is how a shell ends a line and not something the person
	// typed; the newlines inside are theirs and stay.
	text := strings.TrimRight(string(data), "\n")
	return &text, nil
}

// readContent reads bytes, exactly as they are. It is not readText: a Markdown
// body loses nothing by dropping a trailing blank line, and a file somebody
// stored loses its last byte.
func readContent(stdin io.Reader, path string) ([]byte, error) {
	return read(stdin, path)
}

func read(stdin io.Reader, path string) ([]byte, error) {
	var data []byte
	var err error

	if path == "-" {
		data, err = io.ReadAll(stdin)
	} else {
		data, err = os.ReadFile(path)
	}

	if err != nil {
		return nil, &config.UsageError{Message: fmt.Sprintf("cannot read %s: %v", path, err)}
	}

	return data, nil
}
