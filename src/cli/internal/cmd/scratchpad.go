package cmd

import (
	"context"
	"fmt"
	"io"
	"strings"
	"unicode"

	"github.com/google/uuid"
	openapi_types "github.com/oapi-codegen/runtime/types"
	"github.com/spf13/cobra"

	"github.com/datavisionzero/personalaffe/src/cli/internal/api"
	"github.com/datavisionzero/personalaffe/src/cli/internal/client"
	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/render"
)

// firstLine is how much of an entry a list shows. The whole text is what `show`
// is for, and a list that wrapped every entry over four lines would not be a
// list.
const firstLine = 60

func newScratchpad(g *globals) *cobra.Command {
	scratchpad := &cobra.Command{
		Use:   "scratchpad",
		Short: "Temporary plain text, put down on one device and read on another.",
		Long: "Plain text and nothing around it: no title, no tags, no Markdown, no folder\n" +
			"(docs/api.md). An entry that is not pinned is destroyed by the instance a\n" +
			"configured period after it was last changed; pinning is how one is kept.\n\n" +
			"Deleting here is final. This is the one place in personalaffe where agent\n" +
			"access destroys something for good — a Scratchpad entry is never in the\n" +
			"Trash and there is no way back from `rm`.",
	}

	scratchpad.AddCommand(
		newScratchpadList(g),
		newScratchpadAdd(g),
		newScratchpadShow(g),
		newScratchpadEdit(g),
		newScratchpadPin(g, true),
		newScratchpadPin(g, false),
		newScratchpadRemove(g))

	return scratchpad
}

func newScratchpadList(g *globals) *cobra.Command {
	var limit int

	list := &cobra.Command{
		Use:   "list",
		Short: "What is in the Scratchpad, newest capture first.",
		Long: "One line per entry, tab separated: the id, when it was captured, whether it\n" +
			"is pinned, when it expires, and the first line of the text, shortened. The\n" +
			"whole text is what `show` is for.\n\n" +
			"An empty Scratchpad writes nothing to stdout and says so on stderr, so a\n" +
			"pipeline reading it gets nothing rather than a sentence.",
		Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			return g.scratchpadList(cmd.Context(), limit)
		},
	}

	list.Flags().IntVar(&limit, "limit", 0, "at most this many entries")

	return list
}

func newScratchpadAdd(g *globals) *cobra.Command {
	var textFile string
	var pinned bool

	add := &cobra.Command{
		Use:   "add --text-file FILE|-",
		Short: "Put a piece of text down.",
		Long: "The text comes from a file or from stdin, never from an editor:\n\n" +
			"    pea scratchpad add --text-file note.txt\n" +
			"    cat note.txt | pea scratchpad add --text-file -\n\n" +
			"A trailing newline is how a shell ends a line and is dropped; the newlines\n" +
			"inside are the person's and stay. Text that is not ASCII survives unchanged.\n\n" +
			"What goes to stdout is the id of what was written, so that a script can hold\n" +
			"on to it; --json is the entry as the instance answered it.",
		Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			return g.scratchpadAdd(cmd.Context(), textFile, pinned)
		},
	}

	add.Flags().StringVar(&textFile, "text-file", "",
		"the file the text comes from, or - for stdin")
	add.Flags().BoolVar(&pinned, "pinned", false, "pin it at capture, so that it never expires")

	return add
}

func newScratchpadShow(g *globals) *cobra.Command {
	show := &cobra.Command{
		Use:   "show ID",
		Short: "The text of one entry, and nothing else.",
		Long: "The text goes to stdout byte for byte, so that\n\n" +
			"    pea scratchpad show ID > note.txt\n\n" +
			"is the round trip of `add --text-file`. Whatever a person needs told goes to\n" +
			"stderr. --json is the entry as the instance answered it.",
		Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.scratchpadShow(cmd.Context(), args[0])
		},
	}

	return show
}

func newScratchpadEdit(g *globals) *cobra.Command {
	var textFile string
	var pinned bool
	var ifMatch string

	edit := &cobra.Command{
		Use:   "edit ID --text-file FILE|-",
		Short: "Replace an entry's text, its pin, or both.",
		Long: "A write says which version it replaces (docs/api.md). pea reads the entry\n" +
			"first and sends the version it found, so nothing has to hold a timestamp by\n" +
			"hand; --if-match skips that read where pea does not otherwise need the\n" +
			"entry. A version that is no longer the entry's is exit 6.\n\n" +
			"The pin is carried forward unless --pinned says otherwise: one write carries\n" +
			"the text and the pin together, because a pin is a change to the entry.",
		Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.scratchpadEdit(
				cmd.Context(), args[0], textFile, ifMatch, changedPin(cmd, pinned))
		},
	}

	edit.Flags().StringVar(&textFile, "text-file", "",
		"the file the new text comes from, or - for stdin")
	edit.Flags().BoolVar(&pinned, "pinned", false,
		"pin or unpin it as well; left alone when not given")
	edit.Flags().StringVar(&ifMatch, "if-match", "",
		"the version this replaces; read from the instance when not given")

	return edit
}

func newScratchpadPin(g *globals, pinned bool) *cobra.Command {
	verb := "pin"
	what := "Keep an entry: a pinned entry never expires."
	if !pinned {
		verb = "unpin"
		what = "Put an entry back under the clock."
	}

	var ifMatch string

	pin := &cobra.Command{
		Use:   verb + " ID",
		Short: what,
		Long: what + "\n\n" +
			"Unpinning gives an entry a full period from that moment rather than from its\n" +
			"capture, so nothing disappears the instant it is let go.\n\n" +
			"One write carries the text and the pin together, so pea reads the entry and\n" +
			"sends back what it found. --if-match is the version that write replaces, for\n" +
			"a caller that already holds one.",
		Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.scratchpadPin(cmd.Context(), args[0], pinned, ifMatch)
		},
	}

	pin.Flags().StringVar(&ifMatch, "if-match", "",
		"the version this replaces; read from the instance when not given")

	return pin
}

func newScratchpadRemove(g *globals) *cobra.Command {
	var ifMatch string

	remove := &cobra.Command{
		Use:   "rm ID",
		Short: "Destroy one entry. Permanent: there is no way back from this.",
		Long: "A Scratchpad entry is not set aside when it is deleted. It is destroyed, it\n" +
			"never appears in the Trash, and neither the owner nor an operator can bring\n" +
			"it back (docs/api.md). This is the one verb in pea that destroys something.\n\n" +
			"Nothing prompts, because nothing in pea ever does, and there is no --force:\n" +
			"a flag that made this feel dangerous would make every other verb feel safe.\n\n" +
			"It is guarded like any other write: pea reads the entry and sends the version\n" +
			"it found, and --if-match skips that read.",
		Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.scratchpadRemove(cmd.Context(), args[0], ifMatch)
		},
	}

	remove.Flags().StringVar(&ifMatch, "if-match", "",
		"the version this replaces; read from the instance when not given")

	return remove
}

// changedPin is the pin the caller asked for, or nothing where they said
// nothing — which is what lets `edit` carry the pin forward rather than
// silently unpinning whatever it touches.
func changedPin(cmd *cobra.Command, pinned bool) *bool {
	if !cmd.Flags().Changed("pinned") {
		return nil
	}
	return &pinned
}

func (g *globals) scratchpadList(ctx context.Context, limit int) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	params := &api.ReadScratchpadParams{}
	if limit != 0 {
		asked := int32(limit)
		params.Limit = &asked
	}

	resp, err := c.ReadScratchpadWithResponse(ctx, params)
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return err
	}
	if resp.JSON200 == nil {
		return answeredNothing(resp.HTTPResponse.Status)
	}

	if g.json {
		return render.JSON(g.out(), resp.JSON200)
	}

	if len(resp.JSON200.Items) == 0 {
		fmt.Fprintln(g.msg(), "pea: nothing is in the Scratchpad.")
		return nil
	}

	out := g.out()
	for _, entry := range resp.JSON200.Items {
		// One line per entry, tab separated, so that `cut` and `awk` work on it
		// and a terminal still lines it up.
		fmt.Fprintf(out, "%s\t%s\t%s\t%s\t%s\n",
			entry.Id,
			entry.CreatedAt.UTC().Format("2006-01-02T15:04:05Z"),
			pinnedColumn(entry.Pinned),
			expiresColumn(entry),
			shortened(entry.Text))
	}

	if resp.JSON200.HasMore {
		fmt.Fprintln(g.msg(), "pea: there is more; --limit asks for it.")
	}

	return nil
}

func (g *globals) scratchpadAdd(ctx context.Context, textFile string, pinned bool) error {
	if textFile == "" {
		return &config.UsageError{
			Message: "`pea scratchpad add` takes its text from --text-file FILE, or from stdin with -.",
		}
	}

	text, err := readText(g.in(), textFile)
	if err != nil {
		return err
	}

	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	resp, err := c.CaptureEntryWithResponse(
		ctx, api.CaptureEntryJSONRequestBody{Text: text, Pinned: &pinned})
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return err
	}
	if resp.JSON201 == nil {
		return answeredNothing(resp.HTTPResponse.Status)
	}

	if g.json {
		return render.JSON(g.out(), resp.JSON201)
	}

	// The id alone, so that `id=$(pea scratchpad add --text-file -)` is the
	// whole of what a script has to do to hold on to what it wrote.
	fmt.Fprintln(g.out(), resp.JSON201.Id)
	fmt.Fprintln(g.msg(), "pea: captured; it "+expiresSentence(*resp.JSON201)+".")

	return nil
}

func (g *globals) scratchpadShow(ctx context.Context, id string) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	entry, err := readEntry(ctx, c, id)
	if err != nil {
		return err
	}

	if g.json {
		return render.JSON(g.out(), entry)
	}

	// The text and nothing else, byte for byte: `pea scratchpad show ID` is the
	// round trip of `add --text-file`, and a heading in front of it would make
	// it something else.
	_, err = io.WriteString(g.out(), entry.Text)

	return err
}

func (g *globals) scratchpadEdit(
	ctx context.Context, id, textFile, ifMatch string, pinned *bool,
) error {
	if textFile == "" && pinned == nil {
		return &config.UsageError{
			Message: "`pea scratchpad edit` changes the text (--text-file) or the pin (--pinned), or both.",
		}
	}

	text, err := readText(g.in(), textFile)
	if err != nil {
		return err
	}

	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	// The entry is read unless the caller handed over everything the write
	// needs: one PUT carries the text and the pin together, so a write that
	// says nothing about one of them has to find out what it is.
	if ifMatch == "" || text == nil || pinned == nil {
		entry, err := readEntry(ctx, c, id)
		if err != nil {
			return err
		}

		if ifMatch == "" {
			ifMatch = client.EntityTag(entry.UpdatedAt)
		}
		if text == nil {
			text = &entry.Text
		}
		if pinned == nil {
			pinned = &entry.Pinned
		}
	}

	return g.rewrite(ctx, c, id, *text, *pinned, ifMatch)
}

func (g *globals) scratchpadPin(ctx context.Context, id string, pinned bool, ifMatch string) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	// A pin is a change to the entry and the write carries the whole of it, so
	// the text has to be read whatever the caller is holding.
	entry, err := readEntry(ctx, c, id)
	if err != nil {
		return err
	}

	if ifMatch == "" {
		ifMatch = client.EntityTag(entry.UpdatedAt)
	}

	return g.rewrite(ctx, c, id, entry.Text, pinned, ifMatch)
}

func (g *globals) rewrite(
	ctx context.Context, c *client.Client, id, text string, pinned bool, ifMatch string,
) error {
	entryID, err := parseEntryID(id)
	if err != nil {
		return err
	}

	resp, err := c.RewriteEntryWithResponse(
		ctx,
		entryID,
		&api.RewriteEntryParams{IfMatch: ifMatch},
		api.RewriteEntryJSONRequestBody{Text: &text, Pinned: &pinned})
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return err
	}
	if resp.JSON200 == nil {
		return answeredNothing(resp.HTTPResponse.Status)
	}

	if g.json {
		return render.JSON(g.out(), resp.JSON200)
	}

	fmt.Fprintln(g.msg(), "pea: written; it "+expiresSentence(*resp.JSON200)+".")

	return nil
}

func (g *globals) scratchpadRemove(ctx context.Context, id, ifMatch string) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	entryID, err := parseEntryID(id)
	if err != nil {
		return err
	}

	if ifMatch == "" {
		entry, err := readEntry(ctx, c, id)
		if err != nil {
			return err
		}
		ifMatch = client.EntityTag(entry.UpdatedAt)
	}

	resp, err := c.DiscardEntryWithResponse(ctx, entryID, &api.DiscardEntryParams{IfMatch: ifMatch})
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return err
	}

	fmt.Fprintln(g.msg(), "pea: destroyed. A Scratchpad entry is not in the Trash; that one is gone.")

	return nil
}

func readEntry(
	ctx context.Context, c *client.Client, id string,
) (*api.ScratchpadEntryResponse, error) {
	entryID, err := parseEntryID(id)
	if err != nil {
		return nil, err
	}

	resp, err := c.ReadEntryWithResponse(ctx, entryID)
	if err != nil {
		return nil, client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return nil, err
	}
	if resp.JSON200 == nil {
		return nil, answeredNothing(resp.HTTPResponse.Status)
	}

	return resp.JSON200, nil
}

func answeredNothing(status string) error {
	return fmt.Errorf("the instance answered %s with nothing to read", strings.TrimSpace(status))
}

// parseEntryID refuses something that is not an id here rather than sending it,
// so that a mistyped argument is exit 2 with a sentence naming the verb that
// prints them.
func parseEntryID(id string) (openapi_types.UUID, error) {
	parsed, err := uuid.Parse(id)
	if err != nil {
		return openapi_types.UUID{}, &config.UsageError{
			Message: fmt.Sprintf("%q is not an id; `pea scratchpad list` prints them.", id),
		}
	}

	return parsed, nil
}

func pinnedColumn(pinned bool) string {
	if pinned {
		return "pinned"
	}
	return "-"
}

func expiresColumn(entry api.ScratchpadEntryResponse) string {
	if entry.ExpiresAt == nil {
		return "never"
	}
	return entry.ExpiresAt.UTC().Format("2006-01-02T15:04:05Z")
}

func expiresSentence(entry api.ScratchpadEntryResponse) string {
	if entry.ExpiresAt == nil {
		return "is pinned and will not expire"
	}
	return "expires " + entry.ExpiresAt.UTC().Format("2006-01-02T15:04:05Z")
}

// shortened is the first line of an entry, cut to something a list can hold.
// Control characters are replaced rather than written: a tab or a carriage
// return in the text would otherwise break the columns the line is made of.
func shortened(text string) string {
	line, rest, cut := strings.Cut(text, "\n")

	line = strings.Map(func(r rune) rune {
		if unicode.IsControl(r) {
			return ' '
		}
		return r
	}, line)

	runes := []rune(line)
	if len(runes) > firstLine {
		return string(runes[:firstLine]) + "…"
	}

	if cut && rest != "" {
		return line + " …"
	}

	return line
}
