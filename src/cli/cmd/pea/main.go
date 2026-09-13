// pea: personalaffe from the console. A client of the public API and nothing
// else (docs/codebase.md), built as one static binary.
package main

import (
	"context"
	"os"
	"os/signal"

	"github.com/datavisionzero/personalaffe/src/cli/internal/cmd"
)

func main() {
	// Ctrl-C, and the SIGINT a container runtime sends: the context every
	// request hangs off is cancelled, and the command ends where it is rather
	// than after a timeout nobody is waiting for.
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt)
	defer stop()

	os.Exit(cmd.Run(ctx, os.Args[1:], cmd.Env{
		Getenv: os.Getenv,
		Stdin:  os.Stdin,
		Stdout: os.Stdout,
		Stderr: os.Stderr,
	}))
}
