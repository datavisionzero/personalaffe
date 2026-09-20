package cmd

import (
	"context"
	"fmt"
	"strings"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/personalaffe/src/cli/internal/api"
	"github.com/datavisionzero/personalaffe/src/cli/internal/client"
	"github.com/datavisionzero/personalaffe/src/cli/internal/render"
)

func newSearch(g *globals) *cobra.Command {
	var application string
	var limit int

	search := &cobra.Command{
		Use:   "search WORDS...",
		Short: "Find something in the applications this credential can read.",
		Long: "Search knowledge pages, tasks, Scratchpad text, file names and bookmarks.\n" +
			"Bookmarks search saved titles, URLs, descriptions and folder paths.\n" +
			"Private folders inherit visibility; --include-private opts in for this call.\n" +
			"What is inside a file is never looked at (docs/api.md, The search).\n\n" +
			"Every word is matched as a beginning and all of them have to be found, so\n" +
			"`pea search arch dec` finds \"Architecture decisions\". There is no query\n" +
			"language: a stray quote or ampersand does nothing at all.\n\n" +
			"An application this credential cannot read, or one the owner has switched\n" +
			"off, contributes nothing and is not an error — the same as `pea trash list`.\n\n" +
			"One line per finding, tab separated: application, id, title, and the snippet\n" +
			"where there is one.",
		Args: cobra.MinimumNArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			// Joined rather than taken as one argument, so that
			// `pea search arch dec` works without quoting — which is how
			// somebody types a search.
			return g.search(cmd.Context(), strings.Join(args, " "), application, limit)
		},
	}

	search.Flags().StringVar(&application, "application", "",
		"one of "+strings.Join(applications, ", ")+"; all of them by default")
	search.Flags().IntVar(&limit, "limit", 0, "at most this many findings")

	return search
}

func (g *globals) search(ctx context.Context, words, application string, limit int) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	params := &api.SearchParams{Q: &words}
	if application != "" {
		chosen, err := namedApplication(application)
		if err != nil {
			return err
		}
		asked := api.SearchParamsApplication(chosen)
		params.Application = &asked
	}
	if limit != 0 {
		asked := int32(limit)
		params.Limit = &asked
	}

	resp, err := c.SearchWithResponse(ctx, params)
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return err
	}
	if resp.JSON200 == nil {
		return fmt.Errorf("the instance answered %s with nothing to read",
			strings.TrimSpace(resp.HTTPResponse.Status))
	}

	if g.json {
		return render.JSON(g.out(), resp.JSON200)
	}

	if len(resp.JSON200.Items) == 0 {
		fmt.Fprintf(g.env.Stderr, "pea: nothing matches %q.\n", resp.JSON200.Query)
		return nil
	}

	out := g.out()
	for _, found := range resp.JSON200.Items {
		// One line each, tab separated like the Trash's, so that `cut` and
		// `awk` work on it and a terminal still lines it up. A snippet is one
		// line here whatever it was in the page: a newline in a column is a
		// row `cut` cannot read.
		fmt.Fprintf(out, "%s\t%s\t%s\t%s\n",
			found.Application, found.Id, oneLine(found.Title), oneLine(snippet(found.Snippet)))
	}

	if resp.JSON200.HasMore {
		fmt.Fprintln(g.env.Stderr, "pea: there is more; --limit asks for it.")
	}

	return nil
}

func snippet(said *string) string {
	if said == nil {
		return ""
	}
	return *said
}

// oneLine keeps a column a column. The instance answers a person's own text and
// a page's first line can carry anything; a tab or a newline in it would break
// the shape this output promises.
func oneLine(text string) string {
	return strings.Join(strings.Fields(text), " ")
}
