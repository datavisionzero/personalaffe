package cmd

import (
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"strings"

	"github.com/datavisionzero/personalaffe/src/cli/internal/api"
	"github.com/datavisionzero/personalaffe/src/cli/internal/client"
	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/render"
	"github.com/google/uuid"
	"github.com/spf13/cobra"
)

func newBookmarks(g *globals) *cobra.Command {
	root := &cobra.Command{Use: "bookmarks", Short: "Save, find and organize HTTP(S) links.", Long: "Saved links and their own folder tree. Private folders hide descendants unless\n--include-private is explicitly set for this invocation. This is a visibility\nfilter, not encryption or a replacement for application permissions.\n\nSearch matches saved titles, URLs, descriptions and folder paths using word\nprefixes and all words. Reading, listing and searching never record openings.\nWrites hold the version just read, or --if-match; conflicts are never retried.\nDeleted items can be recovered with `pea trash restore bookmarks ID`."}
	root.AddCommand(bookmarkList(g, false), bookmarkList(g, true), bookmarkGet(g, false), bookmarkAdd(g), bookmarkEdit(g, false, false), bookmarkEdit(g, false, true), bookmarkRemove(g, false), bookmarkFavorite(g, true), bookmarkFavorite(g, false))
	folders := &cobra.Command{Use: "folders", Short: "The saved-link folder tree; use IDs for parents."}
	folders.AddCommand(bookmarkFolders(g), bookmarkGet(g, true), bookmarkFolderAdd(g), bookmarkEdit(g, true, false), bookmarkEdit(g, true, true), bookmarkRemove(g, true))
	root.AddCommand(folders, bookmarkImport(g), bookmarkExport(g), bookmarkTags(g))
	return root
}

// The generated client builds every request. This small reader applies the same
// transport/problem/version checks to its raw responses and always closes them.
func bookmarkAnswer[T any](response *http.Response, err error) (T, string, error) {
	var value T
	if err != nil {
		return value, "", client.Transport(err)
	}
	defer response.Body.Close()
	body, err := io.ReadAll(response.Body)
	if err != nil {
		return value, "", client.Transport(err)
	}
	if err = client.Check(response, body); err != nil {
		return value, "", err
	}
	if len(body) > 0 {
		if err = json.Unmarshal(body, &value); err != nil {
			return value, "", err
		}
	}
	return value, response.Header.Get("ETag"), nil
}
func bookmarkID(value string) (*uuid.UUID, error) {
	if value == "" || value == "root" || value == "unsorted" {
		return nil, nil
	}
	id, err := parseID(value)
	if err != nil {
		return nil, err
	}
	return &id, nil
}
func bookmarkPrint(g *globals, row api.BookmarkResponse, onlyID bool) error {
	if g.json {
		return render.JSON(g.out(), row)
	}
	if onlyID {
		fmt.Fprintln(g.out(), row.Id)
	} else {
		fmt.Fprintf(g.out(), "%s\t%s\t%s\t%s\tfavorite=%t\tprivate=%t\t%s\n", row.Id, oneLine(row.Title), oneLine(row.Url), bookmarkParent(row.Folder), row.Favorite, row.Private, row.UpdatedAt.Format("2006-01-02T15:04:05.999999Z07:00"))
	}
	return nil
}
func bookmarkParent(id *uuid.UUID) string {
	if id == nil {
		return "-"
	}
	return id.String()
}
func folderPrint(g *globals, row api.BookmarkFolderResponse, onlyID bool) error {
	if g.json {
		return render.JSON(g.out(), row)
	}
	if onlyID {
		fmt.Fprintln(g.out(), row.Id)
	} else {
		fmt.Fprintf(g.out(), "%s\t%s\t%s\tprivate=%t\n", row.Id, bookmarkParent(row.Parent), oneLine(row.Name), row.EffectivePrivate)
	}
	return nil
}
func bookmarkList(g *globals, search bool) *cobra.Command {
	var folder, query, sort string
	var tags []string
	var favorites bool
	var limit, offset int32
	command := &cobra.Command{Use: "ls", Aliases: []string{"list"}, Short: "List saved links; pages report next_offset in JSON.", Args: cobra.NoArgs}
	if search {
		command.Use = "search WORDS..."
		command.Aliases = nil
		command.Short = "Search saved link text and folder paths."
		command.Args = cobra.MinimumNArgs(1)
	}
	command.RunE = func(cmd *cobra.Command, args []string) error {
		if search {
			query = strings.Join(args, " ")
		}
		parent, err := bookmarkID(folder)
		if err != nil {
			return err
		}
		if limit < 1 || limit > 500 || offset < 0 {
			return &config.UsageError{Message: "--limit must be 1..500 and --offset nonnegative"}
		}
		_, c, err := g.asSomebody()
		if err != nil {
			return err
		}
		unsorted := folder == "unsorted"
		result, _, err := bookmarkAnswer[api.BookmarksResponse](c.ListBookmarks(cmd.Context(), &api.ListBookmarksParams{Q: &query, Folder: parent, Favorites: &favorites, Unsorted: &unsorted, Sort: &sort, Limit: &limit, Offset: &offset, Tag: &tags}))
		if err != nil {
			return err
		}
		if g.json {
			return render.JSON(g.out(), result)
		}
		for _, row := range result.Items {
			if err := bookmarkPrint(g, row, false); err != nil {
				return err
			}
		}
		if result.NextOffset != nil {
			fmt.Fprintf(g.env.Stderr, "pea: more bookmarks; continue with --offset %d.\n", *result.NextOffset)
		}
		return nil
	}
	command.Flags().StringArrayVar(&tags, "tag", nil, "required tag; repeat to require all selected tags")
	command.Flags().StringVar(&folder, "folder", "", "folder ID including descendants, or unsorted")
	command.Flags().StringVar(&query, "query", "", "words to search in saved text")
	command.Flags().StringVar(&sort, "sort", "rank", "rank, title, updated or created")
	command.Flags().BoolVar(&favorites, "favorites", false, "only favorites")
	command.Flags().Int32Var(&limit, "limit", 100, "page size, 1..500")
	command.Flags().Int32Var(&offset, "offset", 0, "page offset")
	return command
}
func bookmarkGet(g *globals, folder bool) *cobra.Command {
	return &cobra.Command{Use: "get ID", Short: "Read one item without recording an opening.", Args: cobra.ExactArgs(1), RunE: func(cmd *cobra.Command, args []string) error {
		id, err := parseID(args[0])
		if err != nil {
			return err
		}
		_, c, err := g.asSomebody()
		if err != nil {
			return err
		}
		if folder {
			row, _, err := bookmarkAnswer[api.BookmarkFolderResponse](c.ReadBookmarkFolder(cmd.Context(), id, nil))
			if err != nil {
				return err
			}
			return folderPrint(g, row, false)
		}
		row, _, err := bookmarkAnswer[api.BookmarkResponse](c.ReadBookmark(cmd.Context(), id, nil))
		if err != nil {
			return err
		}
		return bookmarkPrint(g, row, false)
	}}
}
func bookmarkAdd(g *globals) *cobra.Command {
	var title, description, folder string
	var tags []string
	command := &cobra.Command{Use: "add URL", Short: "Save a link; stdout is its stable ID.", Args: cobra.ExactArgs(1), RunE: func(cmd *cobra.Command, args []string) error {
		parent, err := bookmarkID(folder)
		if err != nil {
			return err
		}
		address := args[0]
		if title == "" {
			parsed, err := url.Parse(address)
			if err != nil {
				return &config.UsageError{Message: "invalid URL"}
			}
			title = parsed.Hostname()
		}
		_, c, err := g.asSomebody()
		if err != nil {
			return err
		}
		row, _, err := bookmarkAnswer[api.BookmarkResponse](c.CreateBookmark(cmd.Context(), nil, api.BookmarkRequest{Title: &title, Url: &address, Description: &description, Folder: parent, Tags: &tags}))
		if err != nil {
			return err
		}
		return bookmarkPrint(g, row, true)
	}}
	command.Flags().StringArrayVar(&tags, "tag", nil, "tag to assign; repeat for more tags")
	command.Flags().StringVar(&title, "title", "", "title; defaults to the URL domain")
	command.Flags().StringVar(&description, "description", "", "plain-text description")
	command.Flags().StringVar(&folder, "folder", "", "destination folder ID; unsorted by default")
	return command
}
func bookmarkFolderAdd(g *globals) *cobra.Command {
	var parent string
	var private bool
	command := &cobra.Command{Use: "add NAME", Short: "Create a bookmark folder.", Args: cobra.ExactArgs(1), RunE: func(cmd *cobra.Command, args []string) error {
		target, err := bookmarkID(parent)
		if err != nil {
			return err
		}
		_, c, err := g.asSomebody()
		if err != nil {
			return err
		}
		row, _, err := bookmarkAnswer[api.BookmarkFolderResponse](c.CreateBookmarkFolder(cmd.Context(), nil, api.BookmarkFolderRequest{Name: &args[0], Parent: target, Private: private}))
		if err != nil {
			return err
		}
		return folderPrint(g, row, true)
	}}
	command.Flags().StringVar(&parent, "parent", "", "parent folder ID or root")
	command.Flags().BoolVar(&private, "private", false, "hide this folder and descendants; requires --include-private")
	return command
}
func bookmarkFolders(g *globals) *cobra.Command {
	var limit, offset int32
	command := &cobra.Command{Use: "ls", Aliases: []string{"list"}, Short: "List visible folders with parent IDs.", Args: cobra.NoArgs, RunE: func(cmd *cobra.Command, _ []string) error {
		if limit < 1 || limit > 500 || offset < 0 {
			return &config.UsageError{Message: "--limit must be 1..500 and --offset nonnegative"}
		}
		_, c, err := g.asSomebody()
		if err != nil {
			return err
		}
		result, _, err := bookmarkAnswer[api.BookmarkFoldersResponse](c.ListBookmarkFolders(cmd.Context(), &api.ListBookmarkFoldersParams{Limit: &limit, Offset: &offset}))
		if err != nil {
			return err
		}
		if g.json {
			return render.JSON(g.out(), result)
		}
		for _, row := range result.Items {
			if err := folderPrint(g, row, false); err != nil {
				return err
			}
		}
		if result.NextOffset != nil {
			fmt.Fprintf(g.env.Stderr, "pea: more folders; continue with --offset %d.\n", *result.NextOffset)
		}
		return nil
	}}
	command.Flags().Int32Var(&limit, "limit", 500, "page size, 1..500")
	command.Flags().Int32Var(&offset, "offset", 0, "page offset")
	return command
}
func bookmarkEdit(g *globals, folder, move bool) *cobra.Command {
	var title, address, description, parent, held string
	var tags []string
	var clearTags bool
	var private bool
	command := &cobra.Command{Use: "edit ID", Short: "Change supplied fields while holding the read version.", Args: cobra.ExactArgs(1)}
	if move {
		command.Use = "move ID"
		command.Short = "Move to --folder or --parent; use root or unsorted to clear."
	}
	command.RunE = func(cmd *cobra.Command, args []string) error {
		id, err := parseID(args[0])
		if err != nil {
			return err
		}
		_, c, err := g.asSomebody()
		if err != nil {
			return err
		}
		flag := "folder"
		if folder {
			flag = "parent"
		}
		if move && !cmd.Flags().Changed(flag) {
			return &config.UsageError{Message: "move requires --" + flag}
		}
		target, err := bookmarkID(parent)
		if err != nil {
			return err
		}
		if folder {
			row, version, err := bookmarkAnswer[api.BookmarkFolderResponse](c.ReadBookmarkFolder(cmd.Context(), id, nil))
			if err != nil {
				return err
			}
			if held == "" {
				held = version
			}
			if cmd.Flags().Changed("name") {
				row.Name = title
			}
			if cmd.Flags().Changed("parent") {
				row.Parent = target
			}
			if cmd.Flags().Changed("private") {
				row.Private = private
			}
			changed, _, err := bookmarkAnswer[api.BookmarkFolderResponse](c.ChangeBookmarkFolder(cmd.Context(), id, &api.ChangeBookmarkFolderParams{IfMatch: held}, api.BookmarkFolderRequest{Name: &row.Name, Parent: row.Parent, Private: row.Private}))
			if err != nil {
				return err
			}
			return folderPrint(g, changed, true)
		}
		row, version, err := bookmarkAnswer[api.BookmarkResponse](c.ReadBookmark(cmd.Context(), id, nil))
		if err != nil {
			return err
		}
		if held == "" {
			held = version
		}
		if clearTags || cmd.Flags().Changed("tag") {
			row.Tags = append([]string{}, tags...)
		}
		if cmd.Flags().Changed("title") {
			row.Title = title
		}
		if cmd.Flags().Changed("link") {
			row.Url = address
		}
		if cmd.Flags().Changed("description") {
			row.Description = description
		}
		if cmd.Flags().Changed("folder") {
			row.Folder = target
		}
		changed, _, err := bookmarkAnswer[api.BookmarkResponse](c.ChangeBookmark(cmd.Context(), id, &api.ChangeBookmarkParams{IfMatch: held}, api.BookmarkRequest{Title: &row.Title, Url: &row.Url, Description: &row.Description, Folder: row.Folder, Tags: &row.Tags}))
		if err != nil {
			return err
		}
		return bookmarkPrint(g, changed, true)
	}
	if folder {
		command.Flags().StringVar(&title, "name", "", "new folder name")
		command.Flags().StringVar(&parent, "parent", "", "new parent ID or root")
		command.Flags().BoolVar(&private, "private", false, "explicit private setting; inherited privacy cannot be overridden")
	} else {
		command.Flags().StringArrayVar(&tags, "tag", nil, "replace tags with this set; repeat for more tags")
		command.Flags().BoolVar(&clearTags, "clear-tags", false, "remove all tags unless new --tag values are supplied")
		command.Flags().StringVar(&title, "title", "", "new title")
		command.Flags().StringVar(&address, "link", "", "new HTTP(S) URL")
		command.Flags().StringVar(&description, "description", "", "new description, including empty")
		command.Flags().StringVar(&parent, "folder", "", "new folder ID or unsorted")
	}
	command.Flags().StringVar(&held, "if-match", "", "expected ETag; otherwise uses the version read for this edit")
	return command
}
func bookmarkRemove(g *globals, folder bool) *cobra.Command {
	var held string
	command := &cobra.Command{Use: "rm ID", Short: "Move to Trash; --if-match can supply a previously read version.", Args: cobra.ExactArgs(1), RunE: func(cmd *cobra.Command, args []string) error {
		id, err := parseID(args[0])
		if err != nil {
			return err
		}
		_, c, err := g.asSomebody()
		if err != nil {
			return err
		}
		if folder {
			if held == "" {
				_, held, err = bookmarkAnswer[api.BookmarkFolderResponse](c.ReadBookmarkFolder(cmd.Context(), id, nil))
				if err != nil {
					return err
				}
			}
			_, _, err = bookmarkAnswer[any](c.DeleteBookmarkFolder(cmd.Context(), id, &api.DeleteBookmarkFolderParams{IfMatch: held}))
		} else {
			if held == "" {
				_, held, err = bookmarkAnswer[api.BookmarkResponse](c.ReadBookmark(cmd.Context(), id, nil))
				if err != nil {
					return err
				}
			}
			_, _, err = bookmarkAnswer[any](c.DeleteBookmark(cmd.Context(), id, &api.DeleteBookmarkParams{IfMatch: held}))
		}
		if err == nil {
			fmt.Fprintln(g.env.Stderr, "pea: moved to Trash; restore with pea trash restore bookmarks", id)
		}
		return err
	}}
	command.Flags().StringVar(&held, "if-match", "", "expected ETag; read first otherwise")
	return command
}
func bookmarkFavorite(g *globals, favorite bool) *cobra.Command {
	var held, after string
	verb := "favorite"
	if !favorite {
		verb = "unfavorite"
	}
	command := &cobra.Command{Use: verb + " ID", Short: "Pin, unpin or reorder a favorite; --after ID selects its predecessor.", Args: cobra.ExactArgs(1), RunE: func(cmd *cobra.Command, args []string) error {
		id, err := parseID(args[0])
		if err != nil {
			return err
		}
		neighbor, err := bookmarkID(after)
		if err != nil {
			return err
		}
		_, c, err := g.asSomebody()
		if err != nil {
			return err
		}
		if held == "" {
			_, held, err = bookmarkAnswer[api.BookmarkResponse](c.ReadBookmark(cmd.Context(), id, nil))
			if err != nil {
				return err
			}
		}
		row, _, err := bookmarkAnswer[api.BookmarkResponse](c.FavoriteBookmark(cmd.Context(), id, &api.FavoriteBookmarkParams{IfMatch: held}, api.FavoriteBookmarkRequest{Favorite: favorite, After: neighbor}))
		if err != nil {
			return err
		}
		return bookmarkPrint(g, row, true)
	}}
	command.Flags().StringVar(&held, "if-match", "", "expected ETag; read first otherwise")
	command.Flags().StringVar(&after, "after", "", "visible favorite ID; first when omitted")
	return command
}

func bookmarkTags(g *globals) *cobra.Command {
	return &cobra.Command{Use: "tags", Short: "Tag suggestions and counts from currently visible bookmarks.", Args: cobra.NoArgs, RunE: func(cmd *cobra.Command, _ []string) error {
		_, c, err := g.asSomebody()
		if err != nil {
			return err
		}
		tags, _, err := bookmarkAnswer[[]api.BookmarkTagCount](c.ListBookmarkTags(cmd.Context(), nil))
		if err != nil {
			return err
		}
		if g.json {
			return render.JSON(g.out(), tags)
		}
		for _, tag := range tags {
			fmt.Fprintf(g.out(), "%s\t%d\n", oneLine(tag.Name), tag.Count)
		}
		return nil
	}}
}
