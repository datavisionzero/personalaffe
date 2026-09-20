package cmd

import (
	"fmt"
	"io"
	"os"
	"unicode/utf8"

	"github.com/datavisionzero/personalaffe/src/cli/internal/api"
	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/render"
	"github.com/spf13/cobra"
)

const bookmarkHTMLLimit = 2 * 1024 * 1024

func bookmarkImport(g *globals) *cobra.Command {
	var file, folder, hash string
	var confirm bool
	command := &cobra.Command{Use: "import --file FILE|-", Short: "Preview browser HTML; --confirm --preview-hash HASH imports the reviewed plan.", Long: "Parse Netscape bookmark HTML as data. Limits: 2 MiB UTF-8, 5000 entries,\n32 folder levels and 100000 visible entries in the existing collection.\nInvalid links are reported; exact URL duplicates in the same folder are skipped.\nNothing is overwritten. Read the preview, then repeat with --confirm and its\n--preview-hash. A changed collection requires a new preview.\n\nBrowser HTML loses private settings, favorites, tags and reading status.\nFor a private export, choose a private --folder and --include-private.", Args: cobra.NoArgs, RunE: func(cmd *cobra.Command, _ []string) error {
		if file == "" || (confirm && hash == "") {
			return &config.UsageError{Message: "--file is required; confirmation also needs the preview's --preview-hash"}
		}
		target, err := bookmarkID(folder)
		if err != nil {
			return err
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
		bytes, err := io.ReadAll(io.LimitReader(input, bookmarkHTMLLimit+1))
		if err != nil {
			return err
		}
		if len(bytes) > bookmarkHTMLLimit || !utf8.Valid(bytes) {
			return &config.UsageError{Message: "HTML must be valid UTF-8 and no larger than 2 MiB"}
		}
		html := string(bytes)
		_, c, err := g.asSomebody()
		if err != nil {
			return err
		}
		body := api.BookmarkImportRequest{Html: &html, Folder: target}
		if confirm {
			body.PreviewHash = &hash
			result, _, err := bookmarkAnswer[api.BookmarkImportResult](c.ImportBookmarks(cmd.Context(), nil, body))
			if err != nil {
				return err
			}
			if g.json {
				return render.JSON(g.out(), result)
			}
			fmt.Fprintf(g.out(), "imported=%d\tfolders=%d\tskipped_duplicates=%d\trejected=%d\n", result.ImportedBookmarks, result.CreatedFolders, result.SkippedDuplicates, len(result.Rejected))
			return nil
		}
		result, _, err := bookmarkAnswer[api.BookmarkImportPreview](c.PreviewBookmarkImport(cmd.Context(), nil, body))
		if err != nil {
			return err
		}
		if g.json {
			return render.JSON(g.out(), result)
		}
		fmt.Fprintf(g.out(), "valid=%d\tfolders=%d\tnew=%d\tnew_folders=%d\tskipped_duplicates=%d\trejected=%d\npreview_hash=%s\n", result.ValidBookmarks, result.Folders, result.NewBookmarks, result.NewFolders, result.SkippedDuplicates, len(result.Rejected), result.PreviewHash)
		for _, rejected := range result.Rejected {
			fmt.Fprintf(g.env.Stderr, "pea: entry %d: %s\n", rejected.Entry, rejected.Reason)
		}
		return nil
	}}
	command.Flags().StringVar(&file, "file", "", "HTML file, or - for stdin")
	command.Flags().StringVar(&folder, "folder", "", "destination folder ID; root by default")
	command.Flags().BoolVar(&confirm, "confirm", false, "apply the reviewed preview atomically")
	command.Flags().StringVar(&hash, "preview-hash", "", "hash from the preview to confirm")
	return command
}
func bookmarkExport(g *globals) *cobra.Command {
	var out, folder string
	var private bool
	command := &cobra.Command{Use: "export [--out FILE|-]", Short: "Export browser HTML; private export additionally needs --export-private.", Long: "Browser HTML is unencrypted and loses private settings, favorites, tags and\nreading status; it is not a backup. Only public content is exported by default,\neven with --include-private. Private export needs both --include-private and\n--export-private. Limits: 5000 entries and 2 MiB; choose a smaller --folder\nif needed. Files are created with private permissions and never overwritten.", Args: cobra.NoArgs, RunE: func(cmd *cobra.Command, _ []string) error {
		if private && !g.includePrivate {
			return &config.UsageError{Message: "--export-private requires --include-private"}
		}
		if g.json && out != "-" {
			return &config.UsageError{Message: "choose --json or --out FILE, not both"}
		}
		target, err := bookmarkID(folder)
		if err != nil {
			return err
		}
		_, c, err := g.asSomebody()
		if err != nil {
			return err
		}
		result, _, err := bookmarkAnswer[api.BookmarkExport](c.ExportBookmarks(cmd.Context(), &api.ExportBookmarksParams{Folder: target, IncludePrivate: &private}))
		if err != nil {
			return err
		}
		if g.json {
			return render.JSON(g.out(), result)
		}
		fmt.Fprintln(g.env.Stderr, "pea:", result.Warning)
		if out == "-" {
			_, err = io.WriteString(g.out(), result.Html)
			return err
		}
		file, err := os.OpenFile(out, os.O_WRONLY|os.O_CREATE|os.O_EXCL, 0o600)
		if err != nil {
			return err
		}
		_, writeErr := io.WriteString(file, result.Html)
		closeErr := file.Close()
		if writeErr != nil {
			return writeErr
		}
		return closeErr
	}}
	command.Flags().StringVar(&out, "out", "-", "new file, or - for stdout")
	command.Flags().StringVar(&folder, "folder", "", "folder subtree to export; whole collection by default")
	command.Flags().BoolVar(&private, "export-private", false, "include private contents in the unencrypted output; also requires --include-private")
	return command
}
