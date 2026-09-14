package cmd

import (
	"context"
	"fmt"
	"io"
	"os"
	"strings"
	"time"

	"github.com/google/uuid"
	openapi_types "github.com/oapi-codegen/runtime/types"
	"github.com/spf13/cobra"

	"github.com/datavisionzero/personalaffe/src/cli/internal/api"
	"github.com/datavisionzero/personalaffe/src/cli/internal/client"
	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/render"
)

func newKnowledge(g *globals) *cobra.Command {
	knowledge := &cobra.Command{
		Use:   "knowledge",
		Short: "The owner's lasting notes: Markdown pages in a tree, with a history behind each.",
		Long: "A page is found at its id, which is made once and never changes, so a link\n" +
			"written down keeps working through every rename, move, rewrite and recovery\n" +
			"(docs/api.md).\n\n" +
			"A path like /Reisen/2026 is pea's convenience and never the API's: the tree\n" +
			"is one read and pea walks it here. An id is accepted anywhere a path is.\n\n" +
			"`rm` puts a page in the Trash with everything under it and all of its\n" +
			"history; `pea trash restore knowledge ID` brings it back.",
	}

	knowledge.AddCommand(
		newKnowledgeTree(g),
		newKnowledgeShow(g),
		newKnowledgeNew(g),
		newKnowledgeEdit(g),
		newKnowledgeMove(g),
		newKnowledgeRemove(g),
		newKnowledgeHistory(g),
		newKnowledgeRecover(g),
		newKnowledgeExport(g))

	return knowledge
}

func newKnowledgeTree(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "tree [PATH|ID]",
		Short: "Every page's title and place. The whole base when nothing is named.",
		Long: "One line per page, tab separated: the id, when it last changed, and the\n" +
			"title indented by how deep it sits. A page named as an argument is the top\n" +
			"of what is printed.\n\n" +
			"No body is read: the tree is titles and places, which is what makes it one\n" +
			"request however much the owner has written.",
		Args: cobra.MaximumNArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.knowledgeTree(cmd.Context(), first(args))
		},
	}
}

func newKnowledgeShow(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "show PATH|ID",
		Short: "The Markdown of one page, and nothing else.",
		Long: "The body goes to stdout byte for byte, so\n\n" +
			"    pea knowledge show /Reisen/2026 > page.md\n" +
			"    pea knowledge edit /Reisen/2026 --text-file page.md\n\n" +
			"is a round trip. Whatever a person needs told goes to stderr. --json is the\n" +
			"page as the instance answered it.",
		Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.knowledgeShow(cmd.Context(), args[0])
		},
	}
}

func newKnowledgeNew(g *globals) *cobra.Command {
	var textFile string

	create := &cobra.Command{
		Use:   "new PATH [--text-file FILE|-]",
		Short: "Write a page. The last segment of PATH is its title.",
		Long: "    pea knowledge new /Reisen\n" +
			"    cat notes.md | pea knowledge new /Reisen/2026 --text-file -\n\n" +
			"Every page above the last one has to be there already; a missing one is\n" +
			"exit 3 naming the segment. A page with a title and nothing under it yet is\n" +
			"a page, so --text-file is optional.\n\n" +
			"The id goes to stdout, so that a script can hold on to what it wrote.",
		Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.knowledgeNew(cmd.Context(), args[0], textFile)
		},
	}

	create.Flags().StringVar(&textFile, "text-file", "",
		"the file the Markdown comes from, or - for stdin")

	return create
}

func newKnowledgeEdit(g *globals) *cobra.Command {
	var textFile string
	var title string
	var ifMatch string

	edit := &cobra.Command{
		Use:   "edit PATH|ID [--text-file FILE|-] [--title TITLE]",
		Short: "Rewrite a page, rename it, or both.",
		Long: "One write carries the title, the place and the body together, because all\n" +
			"three are the same row (docs/api.md). What is not given is carried forward,\n" +
			"so --title alone renames and --text-file alone rewrites.\n\n" +
			"A write says which version it replaces: pea reads the page first and sends\n" +
			"the version it found. --if-match skips that read where pea does not\n" +
			"otherwise need the page. A version that is no longer the page's is exit 6.\n\n" +
			"What it replaced is kept: `pea knowledge history` lists it.",
		Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.knowledgeEdit(cmd.Context(), args[0], textFile, changedTitle(cmd, title), ifMatch)
		},
	}

	edit.Flags().StringVar(&textFile, "text-file", "",
		"the file the new Markdown comes from, or - for stdin")
	edit.Flags().StringVar(&title, "title", "", "rename it as well; left alone when not given")
	edit.Flags().StringVar(&ifMatch, "if-match", "",
		"the version this replaces; read from the instance when not given")

	return edit
}

func newKnowledgeMove(g *globals) *cobra.Command {
	var ifMatch string

	move := &cobra.Command{
		Use:   "mv PATH|ID DEST",
		Short: "Move a page, rename it, or both. What is under it goes with it.",
		Long: "DEST is a path. A DEST that names an existing page puts this one under it\n" +
			"with the title it already has; anything else is where it goes and what it\n" +
			"is called:\n\n" +
			"    pea knowledge mv /Architektur /Notizen             # under it\n" +
			"    pea knowledge mv /Architektur /Notizen/Aufbau      # and renamed\n" +
			"    pea knowledge mv /Architektur /Aufbau              # renamed where it is\n\n" +
			"A title already taken where it is going is exit 5, and so is a page put\n" +
			"under itself or a tree that would be too deep.",
		Args: cobra.ExactArgs(2),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.knowledgeMove(cmd.Context(), args[0], args[1], ifMatch)
		},
	}

	move.Flags().StringVar(&ifMatch, "if-match", "",
		"the version this replaces; read from the instance when not given")

	return move
}

func newKnowledgeRemove(g *globals) *cobra.Command {
	var ifMatch string

	remove := &cobra.Command{
		Use:   "rm PATH|ID",
		Short: "Put a page in the Trash, with everything under it and all of its history.",
		Long: "Nothing is destroyed here. The page leaves every ordinary read and can be\n" +
			"restored until its retention runs out; `pea trash restore knowledge ID`\n" +
			"brings it back, and its history comes back with it.\n\n" +
			"Only the owner can remove something from the Trash for good. The one verb\n" +
			"in pea that destroys something is `pea scratchpad rm`.",
		Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.knowledgeRemove(cmd.Context(), args[0], ifMatch)
		},
	}

	remove.Flags().StringVar(&ifMatch, "if-match", "",
		"the version this replaces; read from the instance when not given")

	return remove
}

func newKnowledgeHistory(g *globals) *cobra.Command {
	var revision string

	history := &cobra.Command{
		Use:   "history PATH|ID",
		Short: "What a page used to say, newest first.",
		Long: "One line per version, tab separated: the id, when it stopped being current,\n" +
			"who ended it, and the title it had. Bodies are not read — fifty versions of\n" +
			"a page is not something to print by accident.\n\n" +
			"--revision ID writes that one version's Markdown to stdout instead, so that\n" +
			"a diff is `pea knowledge history ID --revision R | diff - <(pea knowledge\n" +
			"show ID)`.",
		Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.knowledgeHistory(cmd.Context(), args[0], revision)
		},
	}

	history.Flags().StringVar(&revision, "revision", "",
		"write this version's Markdown to stdout instead of listing them")

	return history
}

func newKnowledgeRecover(g *globals) *cobra.Command {
	var ifMatch string

	recover := &cobra.Command{
		Use:   "recover PATH|ID REVISION",
		Short: "Put a previous version back. What is current now becomes one of its own.",
		Long: "Recovering writes forward: the version it replaces is kept, so history only\n" +
			"grows and nothing is lost by undoing something (docs/api.md). It puts back\n" +
			"the title as well as the body and leaves the page where it is.\n\n" +
			"The version it is guarded by is the page's and not the revision's: a\n" +
			"revision never changes, and recovering something read ten minutes ago must\n" +
			"not discard an edit made five minutes ago.",
		Args: cobra.ExactArgs(2),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.knowledgeRecover(cmd.Context(), args[0], args[1], ifMatch)
		},
	}

	recover.Flags().StringVar(&ifMatch, "if-match", "",
		"the version this replaces; read from the instance when not given")

	return recover
}

func newKnowledgeExport(g *globals) *cobra.Command {
	var out string

	export := &cobra.Command{
		Use:   "export [--out FILE|-]",
		Short: "The whole knowledge base, as a zip of Markdown files.",
		Long: "    pea knowledge export --out knowledge.zip\n" +
			"    pea knowledge export --out - | unzip -l -\n\n" +
			"One .md per page at the path its titles make, each opening with YAML front\n" +
			"matter carrying the id, the parent, the real title and the timestamps, plus\n" +
			"a knowledge.json saying the same in one place.\n\n" +
			"Without --out the bytes go to the name the instance gave it in the working\n" +
			"directory, and pea refuses rather than overwrite something there.",
		Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			return g.knowledgeExport(cmd.Context(), out)
		},
	}

	export.Flags().StringVar(&out, "out", "", "where the zip goes, or - for stdout")

	return export
}

// changedTitle is the title the caller asked for, or nothing where they said
// nothing — which is what lets `edit` carry the title forward rather than
// silently renaming whatever it touches.
func changedTitle(cmd *cobra.Command, title string) *string {
	if !cmd.Flags().Changed("title") {
		return nil
	}
	return &title
}

func (g *globals) knowledgeTree(ctx context.Context, from string) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	tree, err := readTree(ctx, c)
	if err != nil {
		return err
	}

	var root *openapi_types.UUID
	if len(split(from)) > 0 {
		page, err := findInTree(ctx, c, tree, from)
		if err != nil {
			return err
		}
		root = &page.Id
	}

	if g.json {
		return render.JSON(g.out(), tree)
	}

	printed := 0
	out := g.out()

	var walk func(parent *openapi_types.UUID, depth int)
	walk = func(parent *openapi_types.UUID, depth int) {
		for _, page := range tree.Pages {
			if !sameParent(page.Parent, parent) {
				continue
			}
			printed++
			fmt.Fprintf(out, "%s\t%s\t%s%s\n",
				page.Id,
				page.UpdatedAt.UTC().Format("2006-01-02T15:04:05Z"),
				strings.Repeat("  ", depth),
				page.Title)
			id := page.Id
			walk(&id, depth+1)
		}
	}

	if root != nil {
		// The page named is the top of what is printed, and it is printed
		// itself: `tree /Reisen` that showed only what is under Reisen would
		// leave the caller unsure whether Reisen is there at all.
		for _, page := range tree.Pages {
			if page.Id == *root {
				printed++
				fmt.Fprintf(out, "%s\t%s\t%s\n",
					page.Id, page.UpdatedAt.UTC().Format("2006-01-02T15:04:05Z"), page.Title)
			}
		}
		walk(root, 1)
	} else {
		walk(nil, 0)
	}

	if printed == 0 {
		fmt.Fprintln(g.msg(), "pea: there are no pages.")
	}

	return nil
}

func (g *globals) knowledgeShow(ctx context.Context, what string) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	page, err := readPage(ctx, c, what)
	if err != nil {
		return err
	}

	if g.json {
		return render.JSON(g.out(), page)
	}

	// The Markdown and nothing else, byte for byte: a heading in front of it
	// would make `show > page.md` something other than a round trip.
	_, err = io.WriteString(g.out(), page.Markdown)

	return err
}

func (g *globals) knowledgeNew(ctx context.Context, path, textFile string) error {
	segments := split(path)
	if len(segments) == 0 {
		return &config.UsageError{Message: "`pea knowledge new` takes the path of the page to write."}
	}

	markdown, err := readText(g.in(), textFile)
	if err != nil {
		return err
	}

	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	tree, err := readTree(ctx, c)
	if err != nil {
		return err
	}

	var parent *openapi_types.UUID
	if len(segments) > 1 {
		above, err := walkTree(tree, segments[:len(segments)-1])
		if err != nil {
			return err
		}
		parent = &above.Id
	}

	title := segments[len(segments)-1]
	body := ""
	if markdown != nil {
		body = *markdown
	}

	resp, err := c.WritePageWithResponse(
		ctx, api.WritePageJSONRequestBody{Title: &title, Parent: parent, Markdown: &body})
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

	fmt.Fprintln(g.out(), resp.JSON201.Id)
	fmt.Fprintf(g.msg(), "pea: wrote %s.\n", resp.JSON201.Title)

	return nil
}

func (g *globals) knowledgeEdit(
	ctx context.Context, what, textFile string, title *string, ifMatch string,
) error {
	if textFile == "" && title == nil {
		return &config.UsageError{
			Message: "`pea knowledge edit` changes the body (--text-file) or the title (--title), or both.",
		}
	}

	markdown, err := readText(g.in(), textFile)
	if err != nil {
		return err
	}

	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	// One PUT carries the title, the place and the body, so a write that says
	// nothing about one of them has to find out what it is.
	page, err := readPage(ctx, c, what)
	if err != nil {
		return err
	}

	if ifMatch == "" {
		ifMatch = client.EntityTag(page.UpdatedAt)
	}
	if markdown == nil {
		markdown = &page.Markdown
	}
	if title == nil {
		title = &page.Title
	}

	return g.rewritePage(ctx, c, page.Id, *title, page.Parent, *markdown, ifMatch)
}

func (g *globals) knowledgeMove(ctx context.Context, what, dest, ifMatch string) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	page, err := readPage(ctx, c, what)
	if err != nil {
		return err
	}

	tree, err := readTree(ctx, c)
	if err != nil {
		return err
	}

	parent, title, err := whereItGoes(tree, dest, page.Title)
	if err != nil {
		return err
	}

	if ifMatch == "" {
		ifMatch = client.EntityTag(page.UpdatedAt)
	}

	return g.rewritePage(ctx, c, page.Id, title, parent, page.Markdown, ifMatch)
}

func (g *globals) rewritePage(
	ctx context.Context,
	c *client.Client,
	id openapi_types.UUID,
	title string,
	parent *openapi_types.UUID,
	markdown, ifMatch string,
) error {
	resp, err := c.RewritePageWithResponse(
		ctx,
		id,
		&api.RewritePageParams{IfMatch: ifMatch},
		api.RewritePageJSONRequestBody{Title: &title, Parent: parent, Markdown: &markdown})
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

	fmt.Fprintf(g.msg(), "pea: written; it is %s.\n", resp.JSON200.Title)

	return nil
}

func (g *globals) knowledgeRemove(ctx context.Context, what, ifMatch string) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	page, err := readPage(ctx, c, what)
	if err != nil {
		return err
	}

	if ifMatch == "" {
		ifMatch = client.EntityTag(page.UpdatedAt)
	}

	resp, err := c.DiscardPageWithResponse(ctx, page.Id, &api.DiscardPageParams{IfMatch: ifMatch})
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return err
	}

	fmt.Fprintf(g.msg(),
		"pea: %s and everything under it are in the Trash. "+
			"`pea trash restore knowledge %s` brings it back.\n",
		page.Title, page.Id)

	return nil
}

func (g *globals) knowledgeHistory(ctx context.Context, what, revision string) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	page, err := readPage(ctx, c, what)
	if err != nil {
		return err
	}

	if revision != "" {
		return g.knowledgeOldVersion(ctx, c, page.Id, revision)
	}

	resp, err := c.ReadHistoryWithResponse(ctx, page.Id)
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
		fmt.Fprintln(g.msg(), "pea: this page has never been changed.")
		return nil
	}

	out := g.out()
	for _, item := range resp.JSON200.Items {
		fmt.Fprintf(out, "%s\t%s\t%s\t%s\n",
			item.Id, item.At.UTC().Format("2006-01-02T15:04:05Z"), describeActor(item.By), item.Title)
	}

	return nil
}

func (g *globals) knowledgeOldVersion(
	ctx context.Context, c *client.Client, page openapi_types.UUID, revision string,
) error {
	id, err := parseRevisionID(revision)
	if err != nil {
		return err
	}

	resp, err := c.ReadOldVersionWithResponse(ctx, page, id)
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

	_, err = io.WriteString(g.out(), resp.JSON200.Markdown)

	return err
}

func (g *globals) knowledgeRecover(ctx context.Context, what, revision, ifMatch string) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	id, err := parseRevisionID(revision)
	if err != nil {
		return err
	}

	page, err := readPage(ctx, c, what)
	if err != nil {
		return err
	}

	if ifMatch == "" {
		ifMatch = client.EntityTag(page.UpdatedAt)
	}

	resp, err := c.RecoverRevisionWithResponse(
		ctx, page.Id, id, &api.RecoverRevisionParams{IfMatch: ifMatch})
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

	fmt.Fprintf(g.msg(),
		"pea: %s is back to that version. What it said until now is a version of its own.\n",
		resp.JSON200.Title)

	return nil
}

func (g *globals) knowledgeExport(ctx context.Context, out string) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	resp, err := c.ExportKnowledgeWithResponse(ctx)
	if err != nil {
		return client.Transport(err)
	}
	if err := client.CheckBytes(resp.HTTPResponse, resp.Body); err != nil {
		return err
	}

	if out == "-" {
		_, err := g.out().Write(resp.Body)
		return err
	}

	if out == "" {
		out = exportName(resp.HTTPResponse.Header.Get("Content-Disposition"))
	}

	handle, err := os.OpenFile(out, os.O_WRONLY|os.O_CREATE|os.O_EXCL, 0o600)
	if err != nil {
		return &config.UsageError{
			Message: fmt.Sprintf("cannot write %s: %v. --out says where the zip goes.", out, err),
		}
	}
	defer handle.Close()

	if _, err := handle.Write(resp.Body); err != nil {
		return err
	}

	fmt.Fprintf(g.msg(), "pea: wrote %d byte(s) to %s.\n", len(resp.Body), out)

	return nil
}

// readTree is the whole hierarchy in one request. Every path pea resolves is
// resolved against this rather than a request per segment: the tree carries no
// bodies, so asking for all of it is what asking for part of it would cost.
func readTree(ctx context.Context, c *client.Client) (*api.TreeResponse, error) {
	resp, err := c.ReadTreeWithResponse(ctx)
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

// readPage is the page a path or an id names, with its Markdown.
func readPage(ctx context.Context, c *client.Client, what string) (*api.PageResponse, error) {
	id, err := uuid.Parse(strings.TrimSpace(what))
	if err != nil {
		tree, err := readTree(ctx, c)
		if err != nil {
			return nil, err
		}

		page, err := findInTree(ctx, c, tree, what)
		if err != nil {
			return nil, err
		}

		id = page.Id
	}

	resp, err := c.ReadPageWithResponse(ctx, id)
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

// findInTree resolves a path of titles, or an id, against a tree already read.
func findInTree(
	_ context.Context, _ *client.Client, tree *api.TreeResponse, what string,
) (*api.OutlineResponse, error) {
	if id, err := uuid.Parse(strings.TrimSpace(what)); err == nil {
		for index := range tree.Pages {
			if tree.Pages[index].Id == id {
				return &tree.Pages[index], nil
			}
		}
		return nil, nothingCalled(what)
	}

	segments := split(what)
	if len(segments) == 0 {
		return nil, &config.UsageError{
			Message: "the top of the tree is not a page; name one under it.",
		}
	}

	return walkTree(tree, segments)
}

// walkTree follows a path of titles from the top. Titles under one page are one
// each whatever their capitals (docs/api.md), so the comparison is the
// instance's rule and not Go's.
func walkTree(tree *api.TreeResponse, segments []string) (*api.OutlineResponse, error) {
	var parent *openapi_types.UUID
	var found *api.OutlineResponse

	for _, segment := range segments {
		found = nil

		for index := range tree.Pages {
			page := &tree.Pages[index]
			if sameParent(page.Parent, parent) && strings.EqualFold(page.Title, segment) {
				found = page
				break
			}
		}

		if found == nil {
			return nil, nothingCalled(segment)
		}

		id := found.Id
		parent = &id
	}

	return found, nil
}

// whereItGoes reads a `mv` target: the page it goes under, and what it is called
// when it lands. A target that names an existing page means "under it", which is
// what mv means everywhere else.
func whereItGoes(
	tree *api.TreeResponse, dest, itsOwn string,
) (*openapi_types.UUID, string, error) {
	segments := split(dest)

	if len(segments) == 0 {
		// `/` is the top of the tree: moved there, keeping its title.
		return nil, itsOwn, nil
	}

	if page, err := walkTree(tree, segments); err == nil {
		id := page.Id
		return &id, itsOwn, nil
	}

	// The last segment names nothing, so it is the new title and the ones
	// before it are where it goes. They have to exist: mv has never made
	// directories.
	title := segments[len(segments)-1]

	if len(segments) == 1 {
		return nil, title, nil
	}

	above, err := walkTree(tree, segments[:len(segments)-1])
	if err != nil {
		return nil, "", err
	}

	id := above.Id

	return &id, title, nil
}

func sameParent(one, other *openapi_types.UUID) bool {
	if one == nil || other == nil {
		return one == nil && other == nil
	}
	return *one == *other
}

func describeActor(actor api.Actor) string {
	if actor.Name != nil && *actor.Name != "" {
		return "agent " + *actor.Name
	}
	return "the owner"
}

// exportName is what the instance called the zip, or a name of pea's own where
// the header says nothing usable. It is never a path: a Content-Disposition is
// something the other end wrote.
func exportName(disposition string) string {
	const marker = "filename="

	at := strings.Index(disposition, marker)
	if at < 0 {
		return defaultExportName()
	}

	name := strings.Trim(strings.Split(disposition[at+len(marker):], ";")[0], `" `)
	if name == "" || strings.ContainsAny(name, `/\`) || name == "." || name == ".." {
		return defaultExportName()
	}

	return name
}

func defaultExportName() string {
	return "knowledge-" + time.Now().UTC().Format("2006-01-02") + ".zip"
}

func parseRevisionID(id string) (openapi_types.UUID, error) {
	parsed, err := uuid.Parse(strings.TrimSpace(id))
	if err != nil {
		return openapi_types.UUID{}, &config.UsageError{
			Message: fmt.Sprintf("%q is not a version; `pea knowledge history` prints them.", id),
		}
	}

	return parsed, nil
}
