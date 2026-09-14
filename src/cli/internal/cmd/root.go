// Package cmd is the command tree: `pea <object> <verb>`, like gh and glab.
// Data goes to stdout, sentences for a person go to stderr, and the exit code
// says what happened (docs/cli.md).
package cmd

import (
	"context"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/personalaffe/src/cli/internal/client"
	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/exit"
	"github.com/datavisionzero/personalaffe/src/cli/internal/keychain"
	"github.com/datavisionzero/personalaffe/src/cli/internal/version"
)

// Env is what a command runs in, so that a test can supply all of it and no
// test ever reads the machine's own environment or writes its files.
type Env struct {
	Getenv   func(string) string
	Stdin    io.Reader
	Stdout   io.Writer
	Stderr   io.Writer
	HTTP     *http.Client
	Keychain keychain.Keychain
}

// Run executes args and returns the exit code. Nothing here is ever
// interactive: pea reads stdin only where a flag says so, never prompts, and
// never opens an editor.
func Run(ctx context.Context, args []string, env Env) int {
	root := newRoot(env)
	root.SetArgs(args)
	root.SetIn(env.Stdin)
	root.SetOut(env.Stdout)
	root.SetErr(env.Stderr)

	if err := root.ExecuteContext(ctx); err != nil {
		return report(env.Stderr, err)
	}
	return exit.OK
}

// coded is an error that says which exit code it is — what pea itself decides,
// where the instance answered nothing to decide it from.
type coded interface {
	error
	ExitCode() int
}

func report(stderr io.Writer, err error) int {
	var failure *client.Failure
	var usage *config.UsageError
	var own coded

	switch {
	case errors.As(err, &failure):
		fmt.Fprintln(stderr, "pea:", failure.Message)
		return failure.Code
	case errors.As(err, &usage):
		fmt.Fprintln(stderr, "pea:", usage.Message)
		return exit.Usage
	case errors.As(err, &own):
		fmt.Fprintln(stderr, "pea:", own.Error())
		return own.ExitCode()
	default:
		fmt.Fprintln(stderr, "pea:", err)
		return exit.Unexpected
	}
}

type globals struct {
	env Env
	// json prints the object as the API answered it.
	json bool
	// address is --url: the first rung of the ladder that answers which
	// instance this is.
	address string
	// insecureHTTP is --insecure-http: a token over plain HTTP to a host that
	// is not loopback, said out loud.
	insecureHTTP bool
}

func newRoot(env Env) *cobra.Command {
	g := &globals{env: env}
	root := &cobra.Command{
		Use:   "pea",
		Short: "personalaffe from the console: the interface for agents and console-minded humans.",
		Long: "One private workspace, reached over the same HTTP API the browser uses.\n" +
			"Every command writes its data to stdout and its sentences to stderr, and\n" +
			"nothing ever prompts: what pea reads, it reads because a flag said so.",
		Version:       version.Version,
		SilenceUsage:  true,
		SilenceErrors: true,
	}

	root.PersistentFlags().BoolVar(&g.json, "json", false, "print the object as the API answered it")
	root.PersistentFlags().StringVar(&g.address, "url", "",
		"the instance, scheme and host; before "+config.EnvURL+" and before the configured one")
	root.PersistentFlags().BoolVar(&g.insecureHTTP, "insecure-http", false,
		"allow plain HTTP to a host that is not loopback; a token then travels in the clear")

	// A credential is never a flag. It would stand in the shell history, in
	// `ps`, and in whatever a CI runner logs about the command it ran; the
	// environment and a file with a mode are the two ways in (docs/cli.md).
	root.SetVersionTemplate("pea {{.Version}}\n")

	// A usage mistake is exit 2, in the words of the flag package rather than a
	// wall of help.
	root.SetFlagErrorFunc(func(_ *cobra.Command, err error) error {
		return &config.UsageError{Message: err.Error()}
	})

	root.AddCommand(
		newVersion(g), newStatus(g), newLogin(g), newLogout(g), newWhoami(g),
		newApplications(g), newTrash(g))

	usageMistakes(root)
	return root
}

// usageMistakes makes an argument mistake exit 2, like a flag mistake, wherever
// it happens. Cobra answers "accepts 1 arg(s), received 0" and "unknown
// command" with a plain error, which `report` would call unexpected — exit 1,
// the code docs/cli.md keeps for a bug in pea. It is done once over the tree
// rather than at each command, so that a verb added later cannot forget it.
func usageMistakes(cmd *cobra.Command) {
	// An object with no verb prints its verbs, which is what cobra does for a
	// command that cannot run. Saying it here makes the command runnable, and
	// that is what lets the check below be reached at all.
	if cmd.HasSubCommands() && !cmd.Runnable() {
		cmd.RunE = func(c *cobra.Command, _ []string) error { return c.Help() }
	}

	switch {
	case cmd.Args != nil:
		check := cmd.Args
		cmd.Args = func(c *cobra.Command, args []string) error {
			if err := check(c, args); err != nil {
				return &config.UsageError{Message: err.Error()}
			}
			return nil
		}
	case cmd.HasSubCommands():
		cmd.Args = func(c *cobra.Command, args []string) error {
			if len(args) > 0 {
				return &config.UsageError{
					Message: fmt.Sprintf("%q is not a verb of `%s`; --help lists them.", args[0], c.CommandPath()),
				}
			}
			return nil
		}
	}

	for _, child := range cmd.Commands() {
		usageMistakes(child)
	}
}

// input is everything the ladders read, gathered in one place: the environment,
// the flags and what is on disk.
func (g *globals) input() (config.Input, error) {
	file, err := g.readConfig()
	if err != nil {
		return config.Input{}, err
	}

	return config.Input{
		Getenv:         g.getenv,
		File:           file,
		Address:        g.address,
		AllowPlainHTTP: g.insecureHTTP,
		Keychain:       g.keychain(),
	}, nil
}

// anonymous is the instance without a credential: the operations of the
// foundation, which take none.
func (g *globals) anonymous() (string, *client.Client, error) {
	in, err := g.input()
	if err != nil {
		return "", nil, err
	}

	address, err := in.ResolveAddress()
	if err != nil {
		return "", nil, err
	}

	c, err := client.New(address, "", g.httpClient())
	return address, c, err
}

func (g *globals) getenv(name string) string {
	if g.env.Getenv == nil {
		return os.Getenv(name)
	}
	return g.env.Getenv(name)
}

// keychain is this machine's store, or the test's. A command never reaches for
// the real one itself.
func (g *globals) keychain() keychain.Keychain {
	if g.env.Keychain == nil {
		return keychain.OfThisMachine()
	}
	return g.env.Keychain
}

// asSomebody is the instance with a credential: everything but the five
// operations outside the door.
func (g *globals) asSomebody() (config.Resolved, *client.Client, error) {
	in, err := g.input()
	if err != nil {
		return config.Resolved{}, nil, err
	}

	resolved, err := config.Resolve(in)
	if err != nil {
		return config.Resolved{}, nil, err
	}

	c, err := client.New(resolved.Address, resolved.Token, g.httpClient())
	return resolved, c, err
}

func (g *globals) httpClient() *http.Client {
	if g.env.HTTP == nil {
		return client.Default()
	}
	return g.env.HTTP
}

func (g *globals) readConfig() (config.File, error) {
	path, err := config.Path(g.getenv)
	if err != nil {
		return config.File{}, err
	}
	return config.Load(path)
}

// msg is where a sentence for a person goes: stderr, so that stdout stays the
// data a pipeline reads (docs/cli.md).
func (g *globals) msg() io.Writer {
	if g.env.Stderr == nil {
		return os.Stderr
	}
	return g.env.Stderr
}

func (g *globals) out() io.Writer {
	if g.env.Stdout == nil {
		return os.Stdout
	}
	return g.env.Stdout
}

func (g *globals) in() io.Reader {
	if g.env.Stdin == nil {
		return os.Stdin
	}
	return g.env.Stdin
}
