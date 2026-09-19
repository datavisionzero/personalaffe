package cmd

import (
	"context"
	"fmt"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/personalaffe/src/cli/internal/client"
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
	_, from, tokenErr := in.ResolveToken(address)

	served := ""
	name := ""
	var versionErr error
	if _, c, clientErr := g.anonymous(); clientErr == nil {
		served, versionErr = g.served(ctx, c)

		// What the owner calls this instance, if they call it anything. It is
		// outside the door like the version is, so this needs no credential —
		// and it is the answer to the question this command exists for, which
		// is which of these am I about to write to. A failure here is dropped
		// rather than reported: a name is a convenience, and a status command
		// that failed because of one would be useless exactly when it matters.
		name, _ = g.named(ctx, c)
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
			"name":     nilIfEmpty(name),
			"version":  map[string]any{"pea": version.Version, "instance": nilIfEmpty(served)},
			"token":    token,
		})
	}

	out := g.out()

	// The name first and the address after it, because the address is what
	// makes the name unambiguous and the name is what makes the address
	// recognisable. An instance nobody has named says what it always said.
	if name != "" {
		render.Field(out, 9, "instance", fmt.Sprintf("%s (%s)", name, address))
	} else {
		render.Field(out, 9, "instance", address)
	}

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

// named is what the owner calls this instance, which GET /api/appearance
// answers without a credential. Empty where they have not named it, where this
// build of the instance is older than the endpoint, or where the read failed.
func (g *globals) named(ctx context.Context, c *client.Client) (string, error) {
	resp, err := c.ReadAppearanceWithResponse(ctx)
	if err != nil {
		return "", client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return "", err
	}
	if resp.JSON200 == nil || resp.JSON200.Title == nil {
		return "", nil
	}

	return *resp.JSON200.Title, nil
}

func nilIfEmpty(value string) any {
	if value == "" {
		return nil
	}
	return value
}
