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

func newWhoami(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "whoami",
		Short: "Who the credential admits, and what it reaches.",
		Long: "Asks GET /api/me, which is the cheapest way to find out that a credential\n" +
			"still works — and, for an agent, exactly which applications it reaches and\n" +
			"whether it may change them.\n\n" +
			"A revoked token and one that never existed answer the same way: exit 7.",
		Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error { return g.whoami(cmd.Context()) },
	}
}

func (g *globals) whoami(ctx context.Context) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	me, err := readMe(ctx, c)
	if err != nil {
		return err
	}

	if g.json {
		return render.JSON(g.out(), me)
	}

	bookmarks := api.Permission("none")
	if me.Permissions.Bookmarks != nil {
		bookmarks = *me.Permissions.Bookmarks
	}
	out := g.out()
	render.Field(out, 11, "kind", string(me.Kind))

	if me.Email != nil && *me.Email != "" {
		render.Field(out, 11, "email", *me.Email)
	}
	if me.Name != nil && *me.Name != "" {
		render.Field(out, 11, "name", *me.Name)
	}

	// One line per application, in the order CONTEXT.md lists them, so that two
	// agents' permissions can be read side by side.
	for _, granted := range []struct {
		application string
		permission  api.Permission
	}{
		{"scratchpad", me.Permissions.Scratchpad},
		{"knowledge", me.Permissions.Knowledge},
		{"tasks", me.Permissions.Tasks},
		{"files", me.Permissions.Files},
		{"bookmarks", bookmarks},
	} {
		render.Field(out, 11, granted.application, string(granted.permission))
	}

	return nil
}

// described is how a person hears who a credential admits: the owner, or an
// agent by the name the owner gave it.
func described(kind api.CallerKind, name *string) string {
	if name != nil && *name != "" {
		return fmt.Sprintf("%s %q", kind, *name)
	}
	return string(kind)
}

// readMe is the one operation both `whoami` and `login` are built on: it says
// whether a credential works, and says who it is.
func readMe(ctx context.Context, c *client.Client) (*api.MeResponse, error) {
	resp, err := c.ReadMeWithResponse(ctx)
	if err != nil {
		return nil, client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return nil, err
	}
	if resp.JSON200 == nil {
		return nil, fmt.Errorf("the instance answered %s with nothing to read",
			strings.TrimSpace(resp.HTTPResponse.Status))
	}

	return resp.JSON200, nil
}
