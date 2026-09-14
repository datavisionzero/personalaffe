package cmd

import (
	"context"
	"fmt"
	"strings"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/personalaffe/src/cli/internal/client"
	"github.com/datavisionzero/personalaffe/src/cli/internal/render"
)

func newApplications(g *globals) *cobra.Command {
	list := &cobra.Command{
		Use:   "applications",
		Short: "The four applications, which are switched on, and what this credential reaches.",
		Long: "A workspace is four applications and the owner can switch each of them off\n" +
			"(docs/api.md). This says which are on and what this credential may do in\n" +
			"each, which is the answer to why an operation was refused.\n\n" +
			"There is no verb here that switches one. That is the owner's alone and pea\n" +
			"holds agent access, the same reason it cannot issue a credential or change a\n" +
			"security setting (docs/cli.md).",
		Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			return g.applications(cmd.Context())
		},
	}

	return list
}

func (g *globals) applications(ctx context.Context) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	resp, err := c.ReadApplicationsWithResponse(ctx)
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

	out := g.out()
	for _, application := range resp.JSON200.Items {
		// One line each, tab separated like the Trash's, so that `cut` and
		// `awk` work on it and a terminal still lines it up.
		fmt.Fprintf(out, "%s\t%s\t%s\n",
			application.Application, switched(application.Enabled), application.Permission)
	}

	return nil
}

// switched is the word for the switch. "off" rather than an empty column,
// because the line is read by a person and a blank says nothing.
func switched(enabled bool) string {
	if enabled {
		return "on"
	}
	return "off"
}
