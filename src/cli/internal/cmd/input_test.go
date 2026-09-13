package cmd

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// What an agent pipes in is bytes, and the two things that break first are text
// that is not ASCII and text with newlines in it. Both are ordinary in a
// personal workspace — a note in German, a page of Markdown — so both are
// checked here before any command reads one.
const written = "Größe: 5 m²\n\nZeile zwei — mit Gedankenstrich\n\tund einem Tabulator\n日本語\n"

func TestTextFromStdinSurvivesUnicodeAndNewlines(t *testing.T) {
	text, err := readText(strings.NewReader(written), "-")
	if err != nil {
		t.Fatal(err)
	}
	if text == nil {
		t.Fatal("want text, got nothing")
	}

	// Everything but the trailing newline, which is how a shell ends a line and
	// not something the person typed.
	if *text != strings.TrimRight(written, "\n") {
		t.Fatalf("what came back is not what went in: %q", *text)
	}
}

func TestTextFromAFileIsTheSameTextAsFromStdin(t *testing.T) {
	path := filepath.Join(t.TempDir(), "note.md")
	if err := os.WriteFile(path, []byte(written), 0o600); err != nil {
		t.Fatal(err)
	}

	fromFile, err := readText(nil, path)
	if err != nil {
		t.Fatal(err)
	}

	fromStdin, err := readText(strings.NewReader(written), "-")
	if err != nil {
		t.Fatal(err)
	}

	if *fromFile != *fromStdin {
		t.Fatalf("a file and stdin disagree: %q vs %q", *fromFile, *fromStdin)
	}
}

func TestNoFlagMeansNoTextAndNotAnEmptyOne(t *testing.T) {
	text, err := readText(strings.NewReader("ignored"), "")
	if err != nil {
		t.Fatal(err)
	}
	if text != nil {
		t.Fatalf("a flag that was not given is not an empty string: %q", *text)
	}
}

func TestContentKeepsEveryByteIncludingTheLastNewline(t *testing.T) {
	content, err := readContent(strings.NewReader(written), "-")
	if err != nil {
		t.Fatal(err)
	}

	// Not readText: a Markdown body loses nothing by dropping a trailing blank
	// line, and a file somebody stored loses its last byte.
	if string(content) != written {
		t.Fatalf("bytes were changed on the way in: %q", string(content))
	}
}

func TestAFileThatIsNotThereIsAUsageMistakeNamingIt(t *testing.T) {
	missing := filepath.Join(t.TempDir(), "nothing.md")

	_, err := readText(nil, missing)
	if err == nil {
		t.Fatal("want a usage error")
	}
	if !strings.Contains(err.Error(), missing) {
		t.Fatalf("the message does not name the file: %v", err)
	}
}

func TestReadingNothingFromStdinIsEmptyAndNotAHang(t *testing.T) {
	text, err := readText(strings.NewReader(""), "-")
	if err != nil {
		t.Fatal(err)
	}
	if *text != "" {
		t.Fatalf("want empty, got %q", *text)
	}
}
