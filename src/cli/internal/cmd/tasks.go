package cmd

import (
	"context"
	"fmt"
	"io"
	"strings"
	"time"

	"github.com/google/uuid"
	openapi_types "github.com/oapi-codegen/runtime/types"
	"github.com/spf13/cobra"

	"github.com/datavisionzero/personalaffe/src/cli/internal/api"
	"github.com/datavisionzero/personalaffe/src/cli/internal/client"
	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/render"
)

// theDay is how a due date is written on the wire and on a terminal. It is a
// date and never a moment (docs/api.md), so nothing here ever turns one into a
// time and back.
const theDay = "2006-01-02"

// noDate is what --due takes to mean "no longer due". An empty string cannot
// say it: not giving the flag at all already means "leave it alone".
const noDate = "none"

func newTasks(g *globals) *cobra.Command {
	tasks := &cobra.Command{
		Use:   "tasks",
		Short: "Personal commitments in named lists, in an order you set.",
		Long: "Multiple named lists, quick capture, a title and an optional description\n" +
			"and due date, and an order you control (docs/api.md).\n\n" +
			"A due date is a date and never a moment: `--due 2026-09-14` is the\n" +
			"fourteenth wherever you are standing. pea sends the day through as it was\n" +
			"typed rather than parsing it into a time.\n\n" +
			"`rm` puts a task in the Trash; `pea trash restore tasks ID` brings it back.",
	}

	tasks.AddCommand(
		newTaskLists(g),
		newTaskNewList(g),
		newTaskList(g),
		newTaskAdd(g),
		newTaskShow(g),
		newTaskEdit(g),
		newTaskDone(g, true),
		newTaskDone(g, false),
		newTaskMove(g),
		newTaskRemove(g))

	return tasks
}

func newTaskLists(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "lists",
		Short: "Every list, with how much is open in each.",
		Long: "One line per list, tab separated: the id, how many are open, how many there\n" +
			"are, and the name.\n\n" +
			"An instance with no lists writes nothing to stdout and says so on stderr.",
		Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			return g.taskLists(cmd.Context())
		},
	}
}

func newTaskNewList(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "new-list NAME",
		Short: "Make a list.",
		Long: "    pea tasks new-list Einkauf\n\n" +
			"Names are one each whatever their capitals; one already taken is exit 5.\n" +
			"The id goes to stdout.",
		Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.taskNewList(cmd.Context(), args[0])
		},
	}
}

func newTaskList(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "ls LIST",
		Short: "What is in a list, in your order.",
		Long: "One line per task, tab separated: the id, whether it is done, the due date,\n" +
			"and the title. Open tasks first and completed ones after, because those are\n" +
			"two different things to be looking at.\n\n" +
			"A due date that has passed is marked. LIST is a name or an id; the name is\n" +
			"resolved against one read of the lists, so the wire carries ids.",
		Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.taskLs(cmd.Context(), args[0])
		},
	}
}

func newTaskAdd(g *globals) *cobra.Command {
	var due string

	add := &cobra.Command{
		Use:   "add LIST TITLE",
		Short: "Capture a task at the end of a list.",
		Long: "    pea tasks add Einkauf \"Milch holen\"\n" +
			"    pea tasks add Einkauf \"Brot holen\" --due 2026-09-14\n\n" +
			"The title is an argument because this is the one capture that has to be\n" +
			"quick. A description is `edit --description-file`, like every other body in\n" +
			"pea.\n\n" +
			"It goes at the end: capture is what happens when something occurs to you,\n" +
			"and the order is what you decide afterwards. The id goes to stdout.",
		Args: cobra.ExactArgs(2),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.taskAdd(cmd.Context(), args[0], args[1], due)
		},
	}

	add.Flags().StringVar(&due, "due", "", "the day it is due, as 2026-09-14")

	return add
}

func newTaskShow(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "show ID",
		Short: "One task: its description to stdout, the rest to stderr.",
		Long: "The description goes to stdout byte for byte, so that\n\n" +
			"    pea tasks show ID > note.md\n\n" +
			"is the round trip of `edit --description-file`. What a person needs told —\n" +
			"the title, the list, the date, whether it is done — goes to stderr.\n" +
			"--json is the task as the instance answered it.",
		Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.taskShow(cmd.Context(), args[0])
		},
	}
}

func newTaskEdit(g *globals) *cobra.Command {
	var title string
	var due string
	var descriptionFile string
	var list string
	var ifMatch string

	edit := &cobra.Command{
		Use:   "edit ID",
		Short: "Change a task: its title, its date, its description, its list.",
		Long: "    pea tasks edit ID --title \"Milch und Brot\"\n" +
			"    pea tasks edit ID --due 2026-09-20\n" +
			"    pea tasks edit ID --due none\n" +
			"    cat note.md | pea tasks edit ID --description-file -\n" +
			"    pea tasks edit ID --list Arbeit\n\n" +
			"One write carries the title, the description, the date, the list, whether\n" +
			"it is done and where it sits, because all of it is one row (docs/api.md).\n" +
			"What is not given is carried forward, so nothing is changed by accident.\n\n" +
			"Completing is `done` and `undone`; where it sits is `mv`.",
		Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.taskEdit(cmd.Context(), args[0], theChange{
				title:           changedTitle(cmd, title),
				due:             changedDue(cmd, due),
				descriptionFile: descriptionFile,
				list:            list,
				ifMatch:         ifMatch,
			})
		},
	}

	edit.Flags().StringVar(&title, "title", "", "what it is called; left alone when not given")
	edit.Flags().StringVar(&due, "due", "",
		"the day it is due, as 2026-09-14, or "+noDate+" to take the date away")
	edit.Flags().StringVar(&descriptionFile, "description-file", "",
		"the file the description comes from, or - for stdin")
	edit.Flags().StringVar(&list, "list", "", "the list it belongs in, by name or id")
	edit.Flags().StringVar(&ifMatch, "if-match", "",
		"the version this replaces; read from the instance when not given")

	return edit
}

func newTaskDone(g *globals, done bool) *cobra.Command {
	verb := "done"
	what := "Complete a task."
	if !done {
		verb = "undone"
		what = "Reopen a completed task."
	}

	var ifMatch string

	command := &cobra.Command{
		Use:   verb + " ID",
		Short: what,
		Long: what + "\n\n" +
			"Completing records when, and reopening forgets it. Ticking something that\n" +
			"is already ticked changes nothing and does not move the moment it happened.\n\n" +
			"It is one write over the whole task, like every other change to one, so pea\n" +
			"reads it first and sends back what it found.",
		Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.taskEdit(cmd.Context(), args[0], theChange{completed: &done, ifMatch: ifMatch})
		},
	}

	command.Flags().StringVar(&ifMatch, "if-match", "",
		"the version this replaces; read from the instance when not given")

	return command
}

func newTaskMove(g *globals) *cobra.Command {
	var after string
	var top bool
	var ifMatch string

	move := &cobra.Command{
		Use:   "mv ID --after ID|--top",
		Short: "Where a task sits in its list.",
		Long: "    pea tasks mv ID --top\n" +
			"    pea tasks mv ID --after OTHER\n\n" +
			"A place is a neighbour and not a number: the task it goes behind, or the\n" +
			"top of the list. The other task has to be in the same list.\n\n" +
			"Moving one task changes one row, so nobody else's version goes stale.",
		Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			if !top && after == "" {
				return &config.UsageError{
					Message: "`pea tasks mv` takes --after ID, or --top for the top of the list.",
				}
			}
			if top && after != "" {
				return &config.UsageError{
					Message: "--top and --after say two different places. Give one of them.",
				}
			}

			return g.taskEdit(cmd.Context(), args[0], theChange{
				after:    after,
				toTheTop: top,
				ifMatch:  ifMatch,
			})
		},
	}

	move.Flags().StringVar(&after, "after", "", "the task it goes behind")
	move.Flags().BoolVar(&top, "top", false, "put it at the top of its list")
	move.Flags().StringVar(&ifMatch, "if-match", "",
		"the version this replaces; read from the instance when not given")

	return move
}

func newTaskRemove(g *globals) *cobra.Command {
	var ifMatch string

	remove := &cobra.Command{
		Use:   "rm ID",
		Short: "Put a task in the Trash.",
		Long: "Nothing is destroyed here. `pea trash restore tasks ID` brings it back, at\n" +
			"the end of its list — where it used to sit is a number the list may have\n" +
			"reused, and the end is the one place that is always free.\n\n" +
			"A whole list is `pea trash` territory as well: there is no verb here that\n" +
			"deletes one, because deleting a list is not something to do by accident.",
		Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.taskRemove(cmd.Context(), args[0], ifMatch)
		},
	}

	remove.Flags().StringVar(&ifMatch, "if-match", "",
		"the version this replaces; read from the instance when not given")

	return remove
}

// theChange is what one `edit`, `done`, `undone` or `mv` is asking to change.
// Everything it says nothing about is carried forward from what the instance
// already has, because one write carries the whole task.
type theChange struct {
	title           *string
	due             *string
	descriptionFile string
	list            string
	completed       *bool
	after           string
	toTheTop        bool
	ifMatch         string
}

// changedDue is the date the caller asked for, or nothing where they said
// nothing — which is what lets every other verb leave a date alone.
func changedDue(cmd *cobra.Command, due string) *string {
	if !cmd.Flags().Changed("due") {
		return nil
	}
	return &due
}

func (g *globals) taskLists(ctx context.Context) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	lists, err := readLists(ctx, c)
	if err != nil {
		return err
	}

	if g.json {
		return render.JSON(g.out(), lists)
	}

	if len(lists.Items) == 0 {
		fmt.Fprintln(g.msg(), "pea: there are no lists.")
		return nil
	}

	out := g.out()
	for _, list := range lists.Items {
		fmt.Fprintf(out, "%s\t%d\t%d\t%s\n", list.Id, list.Open, list.All, list.Name)
	}

	return nil
}

func (g *globals) taskNewList(ctx context.Context, name string) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	resp, err := c.MakeListWithResponse(ctx, api.MakeListJSONRequestBody{Name: &name})
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return err
	}
	if resp.JSON201 == nil {
		return answeredNothing(resp.HTTPResponse.Status)
	}

	if g.json {
		return render.JSON(g.out(), resp.JSON201)
	}

	fmt.Fprintln(g.out(), resp.JSON201.Id)
	fmt.Fprintf(g.msg(), "pea: made %s.\n", resp.JSON201.Name)

	return nil
}

func (g *globals) taskLs(ctx context.Context, named string) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	list, err := resolveList(ctx, c, named)
	if err != nil {
		return err
	}

	tasks, err := readTasks(ctx, c, list.Id)
	if err != nil {
		return err
	}

	if g.json {
		return render.JSON(g.out(), tasks)
	}

	if len(tasks.Items) == 0 {
		fmt.Fprintf(g.msg(), "pea: %s is empty.\n", list.Name)
		return nil
	}

	out := g.out()
	today := time.Now().UTC().Format(theDay)

	// Open first and completed after: two different things to be looking at,
	// which is VISION §6.4's own phrase. The instance answers them in one order
	// so that a caller can still put a reopened task back where it was.
	for _, done := range []bool{false, true} {
		for _, task := range tasks.Items {
			if task.Completed != done {
				continue
			}

			fmt.Fprintf(out, "%s\t%s\t%s\t%s\n",
				task.Id, doneColumn(task.Completed), dueColumn(task, today), task.Title)
		}
	}

	return nil
}

func (g *globals) taskAdd(ctx context.Context, named, title, due string) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	list, err := resolveList(ctx, c, named)
	if err != nil {
		return err
	}

	body := api.CaptureTaskJSONRequestBody{Title: &title}

	if due != "" {
		day, err := parseDay(due)
		if err != nil {
			return err
		}
		body.DueOn = day
	}

	resp, err := c.CaptureTaskWithResponse(ctx, list.Id, body)
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return err
	}
	if resp.JSON201 == nil {
		return answeredNothing(resp.HTTPResponse.Status)
	}

	if g.json {
		return render.JSON(g.out(), resp.JSON201)
	}

	fmt.Fprintln(g.out(), resp.JSON201.Id)
	fmt.Fprintf(g.msg(), "pea: added to %s.\n", list.Name)

	return nil
}

func (g *globals) taskShow(ctx context.Context, id string) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	task, err := readTask(ctx, c, id)
	if err != nil {
		return err
	}

	if g.json {
		return render.JSON(g.out(), task)
	}

	fmt.Fprintf(g.msg(), "pea: %s\t%s\t%s\n",
		doneColumn(task.Completed), dueColumn(*task, time.Now().UTC().Format(theDay)), task.Title)

	// The description and nothing else, byte for byte: a heading in front of it
	// would make `show > note.md` something other than a round trip.
	_, err = io.WriteString(g.out(), task.Description)

	return err
}

func (g *globals) taskEdit(ctx context.Context, id string, change theChange) error {
	description, err := readText(g.in(), change.descriptionFile)
	if err != nil {
		return err
	}

	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	// One write carries the whole task, so a change that says nothing about a
	// field has to find out what it is.
	task, err := readTask(ctx, c, id)
	if err != nil {
		return err
	}

	body := api.ChangeTaskJSONRequestBody{
		List:        task.List,
		Title:       &task.Title,
		Description: &task.Description,
		DueOn:       task.DueOn,
		Completed:   task.Completed,
		After:       task.After,
	}

	if change.title != nil {
		body.Title = change.title
	}
	if description != nil {
		body.Description = description
	}
	if change.completed != nil {
		body.Completed = *change.completed
	}

	if change.due != nil {
		if strings.EqualFold(*change.due, noDate) {
			body.DueOn = nil
		} else {
			day, err := parseDay(*change.due)
			if err != nil {
				return err
			}
			body.DueOn = day
		}
	}

	if change.list != "" {
		list, err := resolveList(ctx, c, change.list)
		if err != nil {
			return err
		}
		if list.Id != task.List {
			body.List = list.Id
			// Somewhere else entirely: the end of the list it is going to,
			// which is where anything arriving in a list goes.
			body.After = lastOf(ctx, c, list.Id)
		}
	}

	if change.toTheTop {
		body.After = nil
	} else if change.after != "" {
		after, err := parseTaskID(change.after)
		if err != nil {
			return err
		}
		body.After = &after
	}

	ifMatch := change.ifMatch
	if ifMatch == "" {
		ifMatch = client.EntityTag(task.UpdatedAt)
	}

	resp, err := c.ChangeTaskWithResponse(ctx, task.Id, &api.ChangeTaskParams{IfMatch: ifMatch}, body)
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return err
	}
	if resp.JSON200 == nil {
		return answeredNothing(resp.HTTPResponse.Status)
	}

	if g.json {
		return render.JSON(g.out(), resp.JSON200)
	}

	fmt.Fprintf(g.msg(), "pea: written; %s is %s.\n",
		resp.JSON200.Title, stateOf(resp.JSON200.Completed))

	return nil
}

func (g *globals) taskRemove(ctx context.Context, id, ifMatch string) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	task, err := readTask(ctx, c, id)
	if err != nil {
		return err
	}

	if ifMatch == "" {
		ifMatch = client.EntityTag(task.UpdatedAt)
	}

	resp, err := c.DiscardTaskWithResponse(ctx, task.Id, &api.DiscardTaskParams{IfMatch: ifMatch})
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return err
	}

	fmt.Fprintf(g.msg(),
		"pea: %s is in the Trash. `pea trash restore tasks %s` brings it back.\n",
		task.Title, task.Id)

	return nil
}

func readLists(ctx context.Context, c *client.Client) (*api.TaskListsResponse, error) {
	resp, err := c.ReadListsWithResponse(ctx)
	if err != nil {
		return nil, client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return nil, err
	}
	if resp.JSON200 == nil {
		return nil, answeredNothing(resp.HTTPResponse.Status)
	}

	return resp.JSON200, nil
}

func readTasks(
	ctx context.Context, c *client.Client, list openapi_types.UUID,
) (*api.TasksResponse, error) {
	resp, err := c.ReadTasksWithResponse(ctx, list)
	if err != nil {
		return nil, client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return nil, err
	}
	if resp.JSON200 == nil {
		return nil, answeredNothing(resp.HTTPResponse.Status)
	}

	return resp.JSON200, nil
}

func readTask(ctx context.Context, c *client.Client, id string) (*api.TaskResponse, error) {
	taskID, err := parseTaskID(id)
	if err != nil {
		return nil, err
	}

	resp, err := c.ReadTaskWithResponse(ctx, taskID)
	if err != nil {
		return nil, client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return nil, err
	}
	if resp.JSON200 == nil {
		return nil, answeredNothing(resp.HTTPResponse.Status)
	}

	return resp.JSON200, nil
}

// resolveList is the list a name or an id names. A name is pea's convenience
// and never the API's: it is resolved against one read of the lists, and the
// wire carries ids.
func resolveList(ctx context.Context, c *client.Client, named string) (*api.TaskListResponse, error) {
	lists, err := readLists(ctx, c)
	if err != nil {
		return nil, err
	}

	if id, err := uuid.Parse(strings.TrimSpace(named)); err == nil {
		for index := range lists.Items {
			if lists.Items[index].Id == id {
				return &lists.Items[index], nil
			}
		}
		return nil, nothingCalled(named)
	}

	// Names under one workspace are one each whatever their capitals
	// (docs/api.md), so a name typed the way somebody remembers it finds the
	// list it means.
	for index := range lists.Items {
		if strings.EqualFold(lists.Items[index].Name, named) {
			return &lists.Items[index], nil
		}
	}

	return nil, nothingCalled(named)
}

// lastOf is the task at the end of a list, or nothing where it is empty. It
// answers nothing rather than an error: a list pea cannot read is a list the
// write is about to be refused over anyway, in the instance's own words.
func lastOf(ctx context.Context, c *client.Client, list openapi_types.UUID) *openapi_types.UUID {
	tasks, err := readTasks(ctx, c, list)
	if err != nil || len(tasks.Items) == 0 {
		return nil
	}

	return &tasks.Items[len(tasks.Items)-1].Id
}

// parseDay refuses something that is not a day here rather than sending it, so
// that a mistyped date is exit 2 with a sentence naming the shape.
func parseDay(due string) (*openapi_types.Date, error) {
	parsed, err := time.Parse(theDay, strings.TrimSpace(due))
	if err != nil {
		return nil, &config.UsageError{
			Message: fmt.Sprintf(
				"%q is not a day. A due date is written 2026-09-14, and %s takes it away.", due, noDate),
		}
	}

	return &openapi_types.Date{Time: parsed}, nil
}

func parseTaskID(id string) (openapi_types.UUID, error) {
	parsed, err := uuid.Parse(strings.TrimSpace(id))
	if err != nil {
		return openapi_types.UUID{}, &config.UsageError{
			Message: fmt.Sprintf("%q is not an id; `pea tasks ls LIST` prints them.", id),
		}
	}

	return parsed, nil
}

func doneColumn(completed bool) string {
	if completed {
		return "done"
	}
	return "open"
}

// dueColumn is the day as the instance wrote it, with a mark where it has
// passed. The string is never turned into a time: a date that went through a
// timezone on its way to a terminal is a date that can be off by one.
func dueColumn(task api.TaskResponse, today string) string {
	if task.DueOn == nil {
		return "-"
	}

	day := task.DueOn.Format(theDay)

	if !task.Completed && day < today {
		return day + "!"
	}

	return day
}

func stateOf(completed bool) string {
	if completed {
		return "done"
	}
	return "open"
}
