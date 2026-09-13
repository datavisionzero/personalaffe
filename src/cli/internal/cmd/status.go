package cmd

import (
	"context"
	"fmt"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/personalaffe/src/cli/internal/render"
	"github.com/datavisionzero/personalaffe/src/cli/internal/version"
)

func newStatus(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "status",
		Short: "Which instance, whether pea has a credential for it, and where each answer came from.",
		Long: "The two questions worth asking before a write — which instance is this,\n" +
			"and where is the credential coming from — with, for each of them, which\n" +
			"rung of the ladder answered (docs/cli.md).\n\n" +
			"It prints where the credential came from and never the credential.",
		Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error { return g.status(cmd.Context()) },
	}
}

func (g *globals) status(ctx context.Context) error {
	in, err := g.input()
	if err != nil {
		return err
	}

	// The instance is the one thing without which there is nothing to say.
	address, err := in.ResolveAddress()
	if err != nil {
		return err
	}

	// Nothing here stops at the first failure: status is the command somebody
	// runs *because* something is wrong, and one broken answer must not take the
	// other one down with it.
	_, from, tokenErr := in.ResolveToken()

	served := ""
	var versionErr error
	if _, c, clientErr := g.anonymous(); clientErr == nil {
		served, versionErr = g.served(ctx, c)
	} else {
		versionErr = clientErr
	}

	if g.json {
		token := map[string]any{"configured": tokenErr == nil}
		if tokenErr == nil {
			token["from"] = from
		}

		return render.JSON(g.out(), map[string]any{
			"instance": address,
			"version":  map[string]any{"pea": version.Version, "instance": nilIfEmpty(served)},
			"token":    token,
		})
	}

	out := g.out()
	render.Field(out, 9, "instance", address)

	switch {
	case versionErr != nil:
		render.Field(out, 9, "version", fmt.Sprintf("pea %s, and the instance did not say: %v", version.Version, versionErr))
	case served != "":
		render.Field(out, 9, "version", fmt.Sprintf("pea %s, instance %s", version.Version, served))
	}

	if tokenErr != nil {
		render.Field(out, 9, "token", fmt.Sprintf("none: %v", tokenErr))
	} else {
		render.Field(out, 9, "token", fmt.Sprintf("from %s", from))
	}

	return nil
}

func nilIfEmpty(value string) any {
	if value == "" {
		return nil
	}
	return value
}
