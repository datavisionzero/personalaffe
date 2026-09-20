package cmd

import (
	"context"
	"fmt"
	"io"
	"strconv"
	"strings"

	openapi_types "github.com/oapi-codegen/runtime/types"
	"github.com/spf13/cobra"

	"github.com/datavisionzero/personalaffe/src/cli/internal/api"
	"github.com/datavisionzero/personalaffe/src/cli/internal/client"
	"github.com/datavisionzero/personalaffe/src/cli/internal/render"
)

func newDashboard(g *globals) *cobra.Command {
	dashboard := &cobra.Command{
		Use:   "dashboard",
		Short: "What is useful or pending: the home page, as lines.",
		Long: "The tiles the owner keeps on their home page, and what is in the ones this\n" +
			"credential can read (docs/api.md, The dashboard). At most five rows each: it\n" +
			"is an entry point, not a report.\n\n" +
			"There is no verb here that shows or hides a tile. The home page is the\n" +
			"owner's and pea holds agent access, the same reason it cannot switch an\n" +
			"application or issue a credential (docs/cli.md).\n\n" +
			"The weather is its own request, and its own verb: `pea weather`.",
		Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			return g.dashboard(cmd.Context())
		},
	}

	return dashboard
}

func newWeather(g *globals) *cobra.Command {
	weather := &cobra.Command{
		Use:   "weather",
		Short: "What it is doing where the owner said.",
		Long: "The one thing in this product that comes from outside it. It is a separate\n" +
			"request from `pea dashboard` on purpose: a home page must never wait on a\n" +
			"server somewhere else (docs/api.md, The weather).\n\n" +
			"No place set, an instance told not to ask, and a provider that did not answer\n" +
			"are all \"nothing to say\" rather than a failure — exit 0, and a sentence on\n" +
			"stderr saying which.\n\n" +
			"There is no verb here that says where. That is the owner's alone.",
		Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			return g.weather(cmd.Context())
		},
	}

	return weather
}

func (g *globals) dashboard(ctx context.Context) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	resp, err := c.ReadDashboardWithResponse(ctx, nil)
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

	home := resp.JSON200
	out := g.out()
	written := false

	if home.Tasks != nil {
		written = section(out, "tasks", len(*home.Tasks), written)
		for _, task := range *home.Tasks {
			// One line each, tab separated like every other list pea prints.
			fmt.Fprintf(out, "%s\t%s\t%s\t%s\n",
				task.Id, dueDay(task.DueOn), oneLine(task.List), oneLine(task.Title))
		}
	}

	if home.Knowledge != nil {
		written = section(out, "knowledge", len(*home.Knowledge), written)
		for _, page := range *home.Knowledge {
			fmt.Fprintf(out, "%s\t%s\t%s\n",
				page.Id, page.UpdatedAt.UTC().Format("2006-01-02"), oneLine(page.Title))
		}
	}

	if home.Scratchpad != nil {
		written = section(out, "scratchpad", len(*home.Scratchpad), written)
		for _, entry := range *home.Scratchpad {
			fmt.Fprintf(out, "%s\t%s\t%s\n", entry.Id, pinnedOrNot(entry.Pinned), oneLine(entry.Preview))
		}
	}

	if home.Files != nil {
		written = section(out, "files", len(*home.Files), written)
		for _, file := range *home.Files {
			fmt.Fprintf(out, "%s\t%s\t%s\n",
				file.Id, strconv.FormatInt(file.Size, 10), oneLine(file.Name))
		}
	}

	if !written {
		fmt.Fprintln(g.env.Stderr, "pea: nothing is on this home page.")
	}

	if drawn(home.Tiles, "weather") {
		fmt.Fprintln(g.env.Stderr, "pea: the weather is on this home page too; `pea weather` asks for it.")
	}

	return nil
}

func (g *globals) weather(ctx context.Context) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	resp, err := c.ReadWeatherWithResponse(ctx)
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

	weather := resp.JSON200

	// Three ways there is nothing to say, and each of them is exit 0 with a
	// sentence rather than an error: none of them is a fault in this instance.
	switch {
	case !weather.Available:
		fmt.Fprintln(g.env.Stderr, "pea: this instance does not ask anybody about the weather.")
		return nil
	case weather.Place == nil || *weather.Place == "":
		fmt.Fprintln(g.env.Stderr, "pea: the owner has not said where the weather is for.")
		return nil
	case weather.Reading == nil:
		fmt.Fprintf(g.env.Stderr, "pea: the provider did not answer about %s.\n", *weather.Place)
		return nil
	}

	reading := *weather.Reading
	out := g.out()

	render.Field(out, 11, "place", *weather.Place)
	render.Field(out, 11, "temperature",
		strconv.FormatFloat(reading.Temperature, 'f', -1, 64)+weather.TemperatureUnit)
	render.Field(out, 11, "sky", reading.Description)
	render.Field(out, 11, "wind",
		strconv.FormatFloat(reading.Wind, 'f', -1, 64)+" "+weather.WindUnit)

	if reading.High != nil && reading.Low != nil {
		render.Field(out, 11, "today",
			strconv.FormatFloat(*reading.Low, 'f', -1, 64)+weather.TemperatureUnit+" to "+
				strconv.FormatFloat(*reading.High, 'f', -1, 64)+weather.TemperatureUnit)
	}

	render.Field(out, 11, "read_at", reading.ReadAt.UTC().Format("2006-01-02T15:04:05Z"))

	// What a free provider is paid in. It goes to stderr with the other
	// sentences, so that stdout stays the data a pipeline reads.
	fmt.Fprintln(g.env.Stderr, "pea: "+weather.Attribution)

	return nil
}

// section writes the heading above a tile's rows, and a blank line between two
// of them, so that four lists on one stream are still four lists.
func section(out io.Writer, name string, rows int, written bool) bool {
	if written {
		fmt.Fprintln(out)
	}

	fmt.Fprintf(out, "# %s (%d)\n", name, rows)

	return true
}

func drawn(tiles []api.TileResponse, name string) bool {
	for _, tile := range tiles {
		if string(tile.Tile) == name {
			return tile.Shown && tile.Offered
		}
	}
	return false
}

func dueDay(on *openapi_types.Date) string {
	if on == nil {
		return "—"
	}
	return on.Format("2006-01-02")
}

func pinnedOrNot(is bool) string {
	if is {
		return "pinned"
	}
	return "-"
}
