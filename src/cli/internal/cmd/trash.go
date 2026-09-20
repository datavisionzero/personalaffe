package cmd

import (
	"context"
	"fmt"
	"strings"

	"github.com/google/uuid"
	"github.com/spf13/cobra"
	openapi_types "github.com/oapi-codegen/runtime/types"

	"github.com/datavisionzero/personalaffe/src/cli/internal/api"
	"github.com/datavisionzero/personalaffe/src/cli/internal/client"
	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/exit"
	"github.com/datavisionzero/personalaffe/src/cli/internal/render"
)

// applications is the closed set of four, in the order CONTEXT.md lists them.
var applications = []string{"scratchpad", "knowledge", "tasks", "files", "bookmarks"}

func newTrash(g *globals) *cobra.Command {
	trash := &cobra.Command{
		Use:   "trash",
		Short: "What was deleted and is still recoverable.",
		Long: "Deleting a knowledge page, a task, a list, a file or a folder sets it aside\n" +
			"rather than destroying it. This is that list, and the way back out of it.\n\n" +
			"There is no verb here that destroys anything. Removing something for good is\n" +
			"the owner's alone and pea holds agent access, the same reason it cannot issue\n" +
			"a credential or change a security setting (docs/cli.md).",
	}

	trash.AddCommand(newTrashList(g), newTrashRestore(g))
	return trash
}

func newTrashList(g *globals) *cobra.Command {
	var application string
	var limit int

	list := &cobra.Command{
		Use:   "list",
		Short: "What is in the Trash, newest deletion first.",
		Long: "Only the applications this credential may read are asked, so an agent sees\n" +
			"its own half of the workspace and nothing beyond it.\n\n" +
			"`updated_at` on an entry is the version `pea trash restore` sends back; it is\n" +
			"read for you unless --if-match says otherwise.",
		Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			return g.trashList(cmd.Context(), application, limit)
		},
	}

	list.Flags().StringVar(&application, "application", "",
		"one of "+strings.Join(applications, ", ")+"; all of them by default")
	list.Flags().IntVar(&limit, "limit", 0, "at most this many entries")

	return list
}

func newTrashRestore(g *globals) *cobra.Command {
	var name string
	var ifMatch string

	restore := &cobra.Command{
		Use:   "restore APPLICATION ID",
		Short: "Put one thing back where it came from.",
		Long: "Needs read/write access to the application it is in.\n\n" +
			"A write says which version it replaces (docs/api.md). pea reads the entry\n" +
			"first and sends that version itself, so nothing has to hold a timestamp by\n" +
			"hand; --if-match skips the read for a caller that already has one.\n\n" +
			"If the place it came from now has something of that name, the restore is\n" +
			"refused as a conflict: --name puts it back under another one. If the folder\n" +
			"it came from is gone for good, it goes to the root and the answer says so.",
		Args: cobra.ExactArgs(2),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.trashRestore(cmd.Context(), args[0], args[1], name, ifMatch)
		},
	}

	restore.Flags().StringVar(&name, "name", "", "restore it under this name")
	restore.Flags().StringVar(&ifMatch, "if-match", "",
		"the version this replaces; read from the Trash when not given")

	return restore
}

func (g *globals) trashList(ctx context.Context, application string, limit int) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	params := &api.ReadTrashParams{}
	if application != "" {
		chosen, err := namedApplication(application)
		if err != nil {
			return err
		}
		asked := api.ReadTrashParamsApplication(chosen)
		params.Application = &asked
	}
	if limit != 0 {
		asked := int32(limit)
		params.Limit = &asked
	}

	trash, err := readTrash(ctx, c, params)
	if err != nil {
		return err
	}

	if g.json {
		return render.JSON(g.out(), trash)
	}

	out := g.out()
	if len(trash.Items) == 0 {
		fmt.Fprintln(g.env.Stderr, "pea: nothing is in the Trash.")
		return nil
	}

	for _, entry := range trash.Items {
		// One line per entry, tab separated, so that `cut` and `awk` work on it
		// and a terminal still lines it up.
		fmt.Fprintf(out, "%s\t%s\t%s\t%s\tby %s\texpires %s\n",
			entry.Application,
			entry.Id,
			entry.Name,
			where(entry.Where),
			describedActor(entry.DeletedBy),
			entry.ExpiresAt.UTC().Format("2006-01-02"))
	}

	if trash.HasMore {
		fmt.Fprintln(g.env.Stderr, "pea: there is more; --limit asks for it.")
	}

	return nil
}

func (g *globals) trashRestore(ctx context.Context, application, id, name, ifMatch string) error {
	chosen, err := namedApplication(application)
	if err != nil {
		return err
	}

	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	entryID, err := parseID(id)
	if err != nil {
		return err
	}

	if ifMatch == "" {
		ifMatch, err = versionOf(ctx, c, chosen, id)
		if err != nil {
			return err
		}
	}

	params := &api.RestoreFromTrashParams{IfMatch: ifMatch}
	if name != "" {
		params.Name = &name
	}

	resp, err := c.RestoreFromTrashWithResponse(
		ctx, api.RestoreFromTrashParamsApplication(chosen), entryID, params)
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

	render.Field(g.out(), 8, "name", resp.JSON200.Name)
	render.Field(g.out(), 8, "where", where(resp.JSON200.Where))

	if resp.JSON200.MovedToTheRoot {
		// Nothing in this product moves the owner's content without saying so.
		fmt.Fprintln(g.env.Stderr,
			"pea: the place it came from is gone for good, so it was put at the root.")
	}

	return nil
}

// versionOf reads the entry out of the Trash so that the write can say which
// version it replaces. It is the read half of the guard, made by pea rather
// than by whoever is calling it: an agent should not have to hold a timestamp.
func versionOf(ctx context.Context, c *client.Client, application, id string) (string, error) {
	asked := api.ReadTrashParamsApplication(application)

	trash, err := readTrash(ctx, c, &api.ReadTrashParams{Application: &asked})
	if err != nil {
		return "", err
	}

	for _, entry := range trash.Items {
		if entry.Id.String() == id {
			return client.EntityTag(entry.UpdatedAt), nil
		}
	}

	if trash.HasMore {
		return "", &client.Failure{
			Code: exit.NotFound,
			Message: fmt.Sprintf(
				"%s is not in the first page of the %s Trash; `pea trash list --limit` or --if-match",
				id, application),
		}
	}

	return "", &client.Failure{
		Code:    exit.NotFound,
		Message: fmt.Sprintf("nothing with the id %s is in the %s Trash", id, application),
	}
}

func readTrash(
	ctx context.Context, c *client.Client, params *api.ReadTrashParams,
) (*api.TrashResponse, error) {
	resp, err := c.ReadTrashWithResponse(ctx, params)
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

// namedApplication refuses a word outside the set here rather than sending it,
// so that a typo is exit 2 with the four words in it instead of a round trip
// and a 400.
func namedApplication(word string) (string, error) {
	for _, application := range applications {
		if word == application {
			return application, nil
		}
	}

	return "", &config.UsageError{
		Message: fmt.Sprintf("%q is not an application; they are %s.",
			word, strings.Join(applications, ", ")),
	}
}

func where(place *string) string {
	if place == nil || *place == "" {
		return "/"
	}
	return *place
}

func describedActor(actor api.Actor) string {
	if actor.Name != nil && *actor.Name != "" {
		return fmt.Sprintf("%s %q", actor.Kind, *actor.Name)
	}
	return string(actor.Kind)
}

// parseID refuses something that is not an id here rather than sending it: a
// mistyped argument is exit 2, which is what the table of docs/cli.md keeps for
// a mistake in the arguments.
func parseID(id string) (openapi_types.UUID, error) {
	parsed, err := uuid.Parse(id)
	if err != nil {
		return openapi_types.UUID{}, &config.UsageError{
			Message: fmt.Sprintf("%q is not an id; `pea trash list` prints them.", id),
		}
	}

	return parsed, nil
}
