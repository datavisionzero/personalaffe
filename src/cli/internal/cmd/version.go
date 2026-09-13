package cmd

import (
	"context"
	"errors"
	"fmt"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/personalaffe/src/cli/internal/client"
	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/render"
	"github.com/datavisionzero/personalaffe/src/cli/internal/version"
)

func newVersion(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "version",
		Short: "pea's version and the instance's, and whether the two fit.",
		Long: "Asks GET /api/version, which is the one operation that answers before\n" +
			"anything has authenticated — so this is the command that works when the\n" +
			"credential is the thing that is wrong.\n\n" +
			"Without an instance to ask it prints pea's own version and stops, which is\n" +
			"what `pea --version` does.",
		Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error { return g.version(cmd.Context()) },
	}
}

func (g *globals) version(ctx context.Context) error {
	_, c, err := g.anonymous()
	if err != nil {
		// Nobody having said which instance this is is not a failure of this
		// command: pea still knows what pea is, and saying so is more use than a
		// refusal. Any other mistake in the address is a mistake about an
		// instance that *was* named, and this command does not carry on past it.
		if !errors.Is(err, config.ErrNoInstance) {
			return err
		}

		if g.json {
			return render.JSON(g.out(), map[string]any{"pea": version.Version, "instance": nil})
		}

		fmt.Fprintln(g.msg(), "pea:", err)
		render.Field(g.out(), 8, "pea", version.Version)
		return nil
	}

	served, err := g.served(ctx, c)
	if err != nil {
		return err
	}

	if g.json {
		return render.JSON(g.out(), map[string]any{"pea": version.Version, "instance": served})
	}

	render.Field(g.out(), 8, "pea", version.Version)
	render.Field(g.out(), 8, "instance", served)
	return nil
}

// served is the instance's own version, which GET /api/version answers without
// a credential.
func (g *globals) served(ctx context.Context, c *client.Client) (string, error) {
	resp, err := c.ReadVersionWithResponse(ctx)
	if err != nil {
		return "", client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return "", err
	}
	if resp.JSON200 == nil {
		return "", nil
	}

	return resp.JSON200.Version, nil
}
