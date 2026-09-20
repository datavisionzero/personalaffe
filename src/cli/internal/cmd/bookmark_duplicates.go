package cmd

import (
	"encoding/json"
	"fmt"
	"io"
	"os"
	"strconv"
	"time"

	"github.com/datavisionzero/personalaffe/src/cli/internal/api"
	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/render"
	"github.com/spf13/cobra"
)

func bookmarkDuplicates(g *globals) *cobra.Command {
	var url string
	var offset, limit, memberOffset int32
	command := &cobra.Command{Use: "duplicates", Short: "Review visible duplicate URLs, including each copy's metadata and version.", Args: cobra.NoArgs, RunE: func(cmd *cobra.Command, _ []string) error {
		if offset < 0 || memberOffset < 0 || limit < 1 || limit > 50 || (memberOffset > 0 && url == "") {
			return &config.UsageError{Message: "use nonnegative offsets, --limit 1..50 and --url for --member-offset"}
		}
		_, c, err := g.asSomebody()
		if err != nil {
			return err
		}
		result, _, err := bookmarkAnswer[api.BookmarkDuplicatesResponse](c.ListBookmarkDuplicates(cmd.Context(), &api.ListBookmarkDuplicatesParams{Url: &url, Offset: &offset, Limit: &limit, MemberOffset: &memberOffset}))
		if err != nil {
			return err
		}
		if g.json {
			return render.JSON(g.out(), result)
		}
		for _, group := range result.Groups {
			fmt.Fprintf(g.out(), "%s\tcopies=%d\n", oneLine(group.Url), group.Count)
			for _, row := range group.Items {
				if err := bookmarkPrint(g, row, false); err != nil {
					return err
				}
				fmt.Fprintf(g.out(), "  description=%s\ttags=%v\tread_later=%t\n", oneLine(row.Description), row.Tags, row.ReadLater)
			}
			if group.NextMemberOffset != nil {
				fmt.Fprintf(g.env.Stderr, "pea: more copies of %s; use --url and --member-offset %d.\n", oneLine(group.Url), *group.NextMemberOffset)
			}
		}
		if result.NextOffset != nil {
			fmt.Fprintf(g.env.Stderr, "pea: more groups; use --offset %d.\n", *result.NextOffset)
		}
		return nil
	}}
	command.Flags().StringVar(&url, "url", "", "one exact URL group, including a single existing copy")
	command.Flags().Int32Var(&offset, "offset", 0, "group offset")
	command.Flags().Int32Var(&limit, "limit", 20, "group page size, 1..50")
	command.Flags().Int32Var(&memberOffset, "member-offset", 0, "copy offset for --url; each page contains up to 50 copies")
	return command
}

func bookmarkCleanup(g *globals) *cobra.Command {
	var file, version string
	var confirm bool
	command := &cobra.Command{Use: "cleanup --selection FILE|- --if-match VERSION --confirm", Short: "Move explicitly selected duplicate copies to Trash atomically.", Long: `Review "bookmarks duplicates --json" first. Write the chosen IDs and reviewed
versions to a JSON selection: {"keep":"ID","remove":[{"id":"ID","updated_at":"TIMESTAMP"}]}.
Pass the keeper's reviewed updated_at or ETag with --if-match, and --confirm.
All versions and visibility are checked before any write. Nothing is retried.
The keeper's metadata and statistics stay unchanged; nothing is merged.
Copies remain recoverable in Trash; their separate statistics are lost only
when they expire or are permanently deleted. Maximum 100 selected copies.`, Args: cobra.NoArgs, RunE: func(cmd *cobra.Command, _ []string) error {
		if file == "" || version == "" || !confirm {
			return &config.UsageError{Message: "cleanup requires --selection, the reviewed keeper's --if-match and --confirm"}
		}
		var input io.Reader = g.env.Stdin
		if file != "-" {
			handle, err := os.Open(file)
			if err != nil {
				return err
			}
			defer handle.Close()
			input = handle
		}
		bytes, err := io.ReadAll(io.LimitReader(input, 65537))
		if err != nil {
			return err
		}
		var selection api.BookmarkCleanupRequest
		if len(bytes) > 65536 || json.Unmarshal(bytes, &selection) != nil || selection.Remove == nil || len(*selection.Remove) < 1 || len(*selection.Remove) > 100 {
			return &config.UsageError{Message: "selection must be JSON with a keeper and 1..100 versioned copies, at most 64 KiB"}
		}
		_, c, err := g.asSomebody()
		if err != nil {
			return err
		}
		if _, parseErr := time.Parse(time.RFC3339Nano, version); parseErr == nil {
			version = strconv.Quote(version)
		}
		result, _, err := bookmarkAnswer[api.BookmarkCleanup](c.CleanupBookmarkDuplicates(cmd.Context(), &api.CleanupBookmarkDuplicatesParams{IfMatch: version}, selection))
		if err != nil {
			return err
		}
		if g.json {
			return render.JSON(g.out(), result)
		}
		fmt.Fprintf(g.out(), "kept=%s\tmoved_to_trash=%d\n", result.Kept, len(result.Removed))
		return nil
	}}
	command.Flags().StringVar(&file, "selection", "", "reviewed JSON selection file, or - for stdin")
	command.Flags().StringVar(&version, "if-match", "", "reviewed keeper version; never replaced with a fresh read")
	command.Flags().BoolVar(&confirm, "confirm", false, "confirm removal of the explicitly selected copies")
	return command
}
