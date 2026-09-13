package cmd

import (
	"context"
	"errors"
	"fmt"
	"strings"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/personalaffe/src/cli/internal/client"
	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/keychain"
	"github.com/datavisionzero/personalaffe/src/cli/internal/render"
)

func newLogin(g *globals) *cobra.Command {
	var tokenFile string

	command := &cobra.Command{
		Use:   "login --token-file FILE",
		Short: "Check an agent token against the instance and keep it in this machine's keychain.",
		Long: "The owner hands out agent access in the browser; this is where the token\n" +
			"it produced is put so that nothing has to carry it in an environment\n" +
			"variable afterwards (docs/cli.md).\n\n" +
			"The token is read from a file, or from stdin for `-`, and never from an\n" +
			"argument: an argument stands in the shell history, in `ps`, and in whatever\n" +
			"a CI runner logs about the command it ran. It is checked against the\n" +
			"instance before it is stored, so that a token that does not work is a\n" +
			"refusal now rather than a puzzle later.\n\n" +
			"There is no password here and there will not be. A browser signs the owner\n" +
			"in; a console is an agent acting on the owner's behalf, which is what\n" +
			"CONTEXT.md calls it.",
		Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			return g.login(cmd.Context(), tokenFile)
		},
	}

	command.Flags().StringVar(&tokenFile, "token-file", "",
		"the file the token is in, or - for stdin")

	return command
}

func (g *globals) login(ctx context.Context, tokenFile string) error {
	if strings.TrimSpace(tokenFile) == "" {
		return &config.UsageError{Message: "no token: --token-file FILE, or --token-file - for stdin. " +
			"A credential is never an argument."}
	}

	in, err := g.input()
	if err != nil {
		return err
	}

	address, err := in.ResolveAddress()
	if err != nil {
		return err
	}

	raw, err := readContent(g.in(), tokenFile)
	if err != nil {
		return err
	}

	token := strings.TrimSpace(string(raw))
	if token == "" {
		return &config.UsageError{Message: fmt.Sprintf("%s holds no token.", tokenFile)}
	}

	// Checked before it is stored. A token that does not work is a refusal now
	// — exit 7, like every other shut door — rather than a puzzle at the next
	// command.
	c, err := client.New(address, token, g.httpClient())
	if err != nil {
		return err
	}

	me, err := readMe(ctx, c)
	if err != nil {
		return err
	}

	if err := g.keychain().Save(address, token); err != nil {
		if errors.Is(err, keychain.ErrNoKeychain) {
			return &config.UsageError{Message: fmt.Sprintf(
				"this machine has no keychain to put it in. The token works: put it in %s, "+
					"or in a file with mode 600 named by `token_file` in the configuration.",
				config.EnvToken)}
		}
		return &config.UsageError{Message: fmt.Sprintf("the keychain would not take it: %v", err)}
	}

	// The instance is written down too, so that the next command needs neither
	// the flag nor the variable.
	if err := g.remember(address); err != nil {
		return err
	}

	if g.json {
		return render.JSON(g.out(), me)
	}

	fmt.Fprintf(g.msg(), "pea: %s, as %s.\n", address, described(me.Kind, me.Name))
	return nil
}

func newLogout(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "logout",
		Short: "Take this machine's token out of the keychain.",
		Long: "Local, and only local. The token itself keeps working: revoking agent\n" +
			"access is the owner's doing, in the browser, and an agent that could revoke\n" +
			"its own credential is an agent deciding something about the instance.\n\n" +
			"A machine that has nothing stored is left as it is, which is the state that\n" +
			"was asked for.",
		Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error { return g.logout() },
	}
}

func (g *globals) logout() error {
	in, err := g.input()
	if err != nil {
		return err
	}

	address, err := in.ResolveAddress()
	if err != nil {
		return err
	}

	if err := g.keychain().Delete(address); err != nil && !errors.Is(err, keychain.ErrNoKeychain) {
		return &config.UsageError{Message: fmt.Sprintf("the keychain would not give it up: %v", err)}
	}

	fmt.Fprintf(g.msg(),
		"pea: %s is out of this machine's keychain. The token still works — "+
			"revoke it where it was issued.\n", address)

	return nil
}

// remember writes the instance into the configuration, which holds no
// credential and never has.
func (g *globals) remember(address string) error {
	path, err := config.Path(g.getenv)
	if err != nil {
		return err
	}

	file, err := config.Load(path)
	if err != nil {
		return err
	}

	if file.Instance == address {
		return nil
	}

	file.Instance = address

	if err := config.Save(path, file); err != nil {
		return &config.UsageError{Message: fmt.Sprintf("%s could not be written: %v", path, err)}
	}

	return nil
}
