package cmd_test

import (
	"encoding/json"
	"strings"
	"testing"

	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/exit"
)

const theHomePage = `{"tiles":[
  {"tile":"tasks","shown":true,"offered":true,"updated_at":"2026-01-01T00:00:00.000000Z"},
  {"tile":"knowledge","shown":true,"offered":true,"updated_at":"2026-01-01T00:00:00.000000Z"},
  {"tile":"scratchpad","shown":false,"offered":true,"updated_at":"2026-01-01T00:00:00.000000Z"},
  {"tile":"files","shown":true,"offered":false,"updated_at":"2026-01-01T00:00:00.000000Z"},
  {"tile":"weather","shown":true,"offered":true,"updated_at":"2026-01-01T00:00:00.000000Z"}],
 "tasks":[
   {"id":"0199f0c7-0000-7000-8000-000000000001","title":"Milch holen","list_id":"0199f0c6-0000-7000-8000-000000000001",
    "list":"Einkauf","due_on":"2026-09-14","updated_at":"2026-09-14T08:30:00.000000Z"},
   {"id":"0199f0c7-0000-7000-8000-000000000002","title":"Irgendwann","list_id":"0199f0c6-0000-7000-8000-000000000001",
    "list":"Einkauf","due_on":null,"updated_at":"2026-09-14T08:30:00.000000Z"}],
 "knowledge":[],
 "scratchpad":null,
 "files":null}`

func TestDashboardDrawsASectionPerTileItWasGiven(t *testing.T) {
	instance := serving(t, "9.9.9", answering(theHomePage))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "dashboard")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}

	// A section that is absent is not drawn at all; one that is empty is drawn
	// and says so. Those are the API's two different answers.
	if !strings.Contains(got.stdout, "# tasks (2)") {
		t.Fatalf("the tasks section is missing: %q", got.stdout)
	}
	if !strings.Contains(got.stdout, "# knowledge (0)") {
		t.Fatalf("an empty drawn tile is not drawn empty: %q", got.stdout)
	}
	if strings.Contains(got.stdout, "# scratchpad") || strings.Contains(got.stdout, "# files") {
		t.Fatalf("a tile that is not drawn was drawn: %q", got.stdout)
	}

	if !strings.Contains(got.stdout, "2026-09-14\tEinkauf\tMilch holen") {
		t.Fatalf("a dated task is not printed with its list: %q", got.stdout)
	}

	// A task with no due date is not overdue and says nothing rather than a
	// blank column.
	if !strings.Contains(got.stdout, "—\tEinkauf\tIrgendwann") {
		t.Fatalf("an undated task is not printed plainly: %q", got.stdout)
	}
}

func TestDashboardSaysWhereTheWeatherIs(t *testing.T) {
	instance := serving(t, "9.9.9", answering(theHomePage))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "dashboard")

	// One request per document: the home page never waits on a provider.
	if len(instance.Requests) != 1 {
		t.Fatalf("pea made %d requests for one home page", len(instance.Requests))
	}
	if !strings.Contains(got.stderr, "pea weather") {
		t.Fatalf("the weather tile is on the page and nobody was told how to see it: %q", got.stderr)
	}
}

func TestDashboardWithNothingOnItSaysSoOnStderr(t *testing.T) {
	instance := serving(t, "9.9.9", answering(
		`{"tiles":[{"tile":"tasks","shown":false,"offered":true,"updated_at":"2026-01-01T00:00:00.000000Z"}],
		  "tasks":null,"knowledge":null,"scratchpad":null,"files":null}`))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "dashboard")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if strings.TrimSpace(got.stdout) != "" {
		t.Fatalf("stdout is not empty: %q", got.stdout)
	}
	if !strings.Contains(got.stderr, "nothing is on this home page") {
		t.Fatalf("nobody was told: %q", got.stderr)
	}
}

func TestDashboardAsJsonIsTheObjectTheApiAnswered(t *testing.T) {
	instance := serving(t, "9.9.9", answering(theHomePage))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "dashboard", "--json")

	var body struct {
		Tiles []map[string]any `json:"tiles"`
	}
	if err := json.Unmarshal([]byte(got.stdout), &body); err != nil {
		t.Fatalf("not JSON: %v (%q)", err, got.stdout)
	}
	if len(body.Tiles) != 5 {
		t.Fatalf("the object is not the one the API answered: %v", body)
	}
}

func TestDashboardHasNoVerbThatShowsOrHidesATile(t *testing.T) {
	instance := serving(t, "9.9.9", answering(theHomePage))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	// The home page is the owner's and pea holds agent access. A verb here
	// would be a verb that can only ever be refused.
	got := run(t, env, "dashboard", "hide", "files")

	if got.code != exit.Usage {
		t.Fatalf("want exit %d, got %d: %s", exit.Usage, got.code, got.stderr)
	}
}

const theWeather = `{"place":"Wuppertal","latitude":51.2563,"longitude":7.1482,
 "units":"metric","temperature_unit":"°C","wind_unit":"km/h","available":true,
 "reading":{"temperature":16.1,"feels_like":16.5,"high":20.3,"low":14.2,"wind":2.2,
   "code":3,"description":"Overcast","day":true,"read_at":"2026-09-16T07:15:00.000000Z"},
 "attribution":"Weather data by Open-Meteo.com",
 "updated_at":"2026-09-15T18:02:11.000000Z"}`

func TestWeatherPrintsWhatItIsDoingAndCreditsWhoSaidSo(t *testing.T) {
	instance := serving(t, "9.9.9", answering(theWeather))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "weather")

	if got.code != exit.OK {
		t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
	}
	if !strings.Contains(got.stdout, "16.1°C") || !strings.Contains(got.stdout, "Overcast") {
		t.Fatalf("the reading is not printed: %q", got.stdout)
	}
	if !strings.Contains(got.stdout, "14.2°C to 20.3°C") {
		t.Fatalf("the day's range is not printed: %q", got.stdout)
	}

	// What a free provider is paid in, and on stderr with the other sentences
	// so that stdout stays the data.
	if !strings.Contains(got.stderr, "Open-Meteo") {
		t.Fatalf("the attribution is missing: %q", got.stderr)
	}
	if strings.Contains(got.stdout, "Open-Meteo") {
		t.Fatalf("a sentence landed on stdout: %q", got.stdout)
	}
}

func TestWeatherWithNothingToSayIsNotAFailure(t *testing.T) {
	for _, one := range []struct {
		name string
		body string
		says string
	}{
		{
			"no place",
			`{"place":null,"latitude":null,"longitude":null,"units":"metric",
			  "temperature_unit":"°C","wind_unit":"km/h","available":true,"reading":null,
			  "attribution":"x","updated_at":"2026-01-01T00:00:00.000000Z"}`,
			"has not said where",
		},
		{
			"not asking",
			`{"place":"Wuppertal","latitude":51.2,"longitude":7.1,"units":"metric",
			  "temperature_unit":"°C","wind_unit":"km/h","available":false,"reading":null,
			  "attribution":"x","updated_at":"2026-01-01T00:00:00.000000Z"}`,
			"does not ask anybody",
		},
		{
			"no answer",
			`{"place":"Wuppertal","latitude":51.2,"longitude":7.1,"units":"metric",
			  "temperature_unit":"°C","wind_unit":"km/h","available":true,"reading":null,
			  "attribution":"x","updated_at":"2026-01-01T00:00:00.000000Z"}`,
			"did not answer",
		},
	} {
		t.Run(one.name, func(t *testing.T) {
			instance := serving(t, "9.9.9", answering(one.body))
			env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

			got := run(t, env, "weather")

			// None of the three is a fault in this instance, so none of them is
			// a non-zero exit.
			if got.code != exit.OK {
				t.Fatalf("want exit 0, got %d: %s", got.code, got.stderr)
			}
			if strings.TrimSpace(got.stdout) != "" {
				t.Fatalf("stdout is not empty: %q", got.stdout)
			}
			if !strings.Contains(got.stderr, one.says) {
				t.Fatalf("nobody was told why: %q", got.stderr)
			}
		})
	}
}

func TestWeatherHasNoVerbThatSaysWhere(t *testing.T) {
	instance := serving(t, "9.9.9", answering(theWeather))
	env := environment(t, map[string]string{config.EnvURL: instance.URL, config.EnvToken: secret})

	got := run(t, env, "weather", "set", "Wuppertal")

	if got.code != exit.Usage {
		t.Fatalf("want exit %d, got %d: %s", exit.Usage, got.code, got.stderr)
	}
}
