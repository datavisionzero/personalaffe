package cmd

import (
	"bytes"
	"context"
	"fmt"
	"os"
	"strings"
	"time"

	"github.com/google/uuid"
	openapi_types "github.com/oapi-codegen/runtime/types"
	"github.com/spf13/cobra"

	"github.com/datavisionzero/personalaffe/src/cli/internal/api"
	"github.com/datavisionzero/personalaffe/src/cli/internal/client"
	"github.com/datavisionzero/personalaffe/src/cli/internal/config"
	"github.com/datavisionzero/personalaffe/src/cli/internal/exit"
	"github.com/datavisionzero/personalaffe/src/cli/internal/render"
)

func newFiles(g *globals) *cobra.Command {
	files := &cobra.Command{
		Use:   "files",
		Short: "The owner's own storage: files in folders, on this instance's disk.",
		Long: "Upload, download, organise and delete. The bytes live on the instance's\n" +
			"volume and the metadata in its database (docs/api.md).\n\n" +
			"A path like /Reisen/2026/bahn.pdf is pea's convenience and never the API's:\n" +
			"pea walks it a segment at a time and the wire carries ids. An id is\n" +
			"accepted anywhere a path is, because that is what the other verbs print.\n\n" +
			"`rm` here is not `scratchpad rm`. A deleted file goes to the Trash and\n" +
			"comes back with `pea trash restore files ID`.",
	}

	files.AddCommand(
		newFilesList(g),
		newFilesPut(g),
		newFilesGet(g),
		newFilesMkdir(g),
		newFilesMove(g),
		newFilesRemove(g))

	return files
}

func newFilesList(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "ls [PATH|ID]",
		Short: "What is in a folder. The top of the tree when nothing is named.",
		Long: "One line per entry, tab separated: the id, whether it is a folder or a\n" +
			"file, its size in bytes, when it last changed, and its name. Folders come\n" +
			"first, and a folder's size column is `-`.\n\n" +
			"An empty folder writes nothing to stdout and says so on stderr, so a\n" +
			"pipeline reading it gets nothing rather than a sentence.",
		Args: cobra.MaximumNArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.filesList(cmd.Context(), first(args))
		},
	}
}

func newFilesPut(g *globals) *cobra.Command {
	var file string
	var to string
	var name string

	put := &cobra.Command{
		Use:   "put --file FILE|- [--to PATH] [--name NAME]",
		Short: "Store a file. The bytes come from a file or from stdin.",
		Long: "    pea files put --file Reisekosten.pdf --to /Reisen/2026\n" +
			"    tar c notes | pea files put --file - --name notes.tar\n\n" +
			"--name is what it is called on the instance; without it, the base name of\n" +
			"--file is used, which is why --file - needs one. --to names the folder,\n" +
			"and the top of the tree is where it goes without one.\n\n" +
			"What goes to stdout is the id of what was stored, so that a script can\n" +
			"hold on to it; --json is the file as the instance answered it. Its bytes\n" +
			"are then at that id for good: renaming it and moving it do not change\n" +
			"where it is downloaded from.",
		Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			return g.filesPut(cmd.Context(), file, to, name)
		},
	}

	put.Flags().StringVar(&file, "file", "", "the file the bytes come from, or - for stdin")
	put.Flags().StringVar(&to, "to", "", "the folder it goes in; the top of the tree when not given")
	put.Flags().StringVar(&name, "name", "", "what it is called on the instance")

	return put
}

func newFilesGet(g *globals) *cobra.Command {
	var out string

	get := &cobra.Command{
		Use:   "get PATH|ID [--out FILE|-]",
		Short: "The bytes of one file, byte for byte.",
		Long: "    pea files get /Reisen/2026/bahn.pdf --out bahn.pdf\n" +
			"    pea files get 0199f0c4-… --out - | sha256sum\n\n" +
			"Nothing is added and nothing is interpreted: --out - is the round trip of\n" +
			"`put --file -`. Without --out the bytes go to the file's own name in the\n" +
			"working directory, and pea refuses rather than overwrite something there.\n\n" +
			"--json is the file's metadata instead of its bytes.",
		Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.filesGet(cmd.Context(), args[0], out)
		},
	}

	get.Flags().StringVar(&out, "out", "", "where the bytes go, or - for stdout")

	return get
}

func newFilesMkdir(g *globals) *cobra.Command {
	var parents bool

	mkdir := &cobra.Command{
		Use:   "mkdir PATH",
		Short: "Make a folder.",
		Long: "    pea files mkdir /Reisen\n" +
			"    pea files mkdir --parents /Reisen/2026/Belege\n\n" +
			"Without --parents, every folder above the last one has to be there\n" +
			"already, and a missing one is exit 3 naming the segment that failed.\n\n" +
			"The id of the folder goes to stdout.",
		Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.filesMkdir(cmd.Context(), args[0], parents)
		},
	}

	mkdir.Flags().BoolVar(&parents, "parents", false, "make the folders above it as well")

	return mkdir
}

func newFilesMove(g *globals) *cobra.Command {
	var ifMatch string

	move := &cobra.Command{
		Use:   "mv PATH|ID DEST",
		Short: "Rename something, move it, or both.",
		Long: "DEST is a path. A DEST that names an existing folder moves the thing into\n" +
			"it under the name it already has; anything else is where it goes and what\n" +
			"it is called:\n\n" +
			"    pea files mv /plan.md /Reisen              # into the folder\n" +
			"    pea files mv /plan.md /Reisen/der-plan.md  # and renamed\n" +
			"    pea files mv /plan.md /der-plan.md         # renamed where it is\n\n" +
			"One write carries the name and the place together, because both are\n" +
			"changes to the same row. A name already taken in the destination is exit 5\n" +
			"and never a silent rename.\n\n" +
			"A write says which version it replaces (docs/api.md): pea reads the thing\n" +
			"first and sends the version it found, and --if-match skips that read.",
		Args: cobra.ExactArgs(2),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.filesMove(cmd.Context(), args[0], args[1], ifMatch)
		},
	}

	move.Flags().StringVar(&ifMatch, "if-match", "",
		"the version this replaces; read from the instance when not given")

	return move
}

func newFilesRemove(g *globals) *cobra.Command {
	var ifMatch string

	remove := &cobra.Command{
		Use:   "rm PATH|ID",
		Short: "Put something in the Trash. `pea trash restore files ID` brings it back.",
		Long: "Nothing is destroyed here. A file or a folder is set aside, leaves every\n" +
			"ordinary read, and can be restored until its retention runs out\n" +
			"(docs/api.md). A folder takes everything in it, under one moment, so the\n" +
			"whole thing comes back together.\n\n" +
			"Only the owner can remove something from the Trash for good, and that is\n" +
			"`pea trash rm` rather than this. The one verb in pea that destroys\n" +
			"something is `pea scratchpad rm`.\n\n" +
			"It is guarded like any other write: pea reads the thing and sends the\n" +
			"version it found, and --if-match skips that read.",
		Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			return g.filesRemove(cmd.Context(), args[0], ifMatch)
		},
	}

	remove.Flags().StringVar(&ifMatch, "if-match", "",
		"the version this replaces; read from the instance when not given")

	return remove
}

func (g *globals) filesList(ctx context.Context, where string) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	folder, err := resolveFolder(ctx, c, where)
	if err != nil {
		return err
	}

	listing, err := readFolder(ctx, c, folder)
	if err != nil {
		return err
	}

	if g.json {
		return render.JSON(g.out(), listing)
	}

	if len(listing.Folders) == 0 && len(listing.Files) == 0 {
		fmt.Fprintln(g.msg(), "pea: the folder is empty.")
		return nil
	}

	out := g.out()

	// Folders first, then files, each already in name order from the instance:
	// that is what a person reading a listing expects and what a script sorting
	// it again can undo.
	for _, one := range listing.Folders {
		fmt.Fprintf(out, "%s\tfolder\t-\t%s\t%s\n",
			one.Id, one.UpdatedAt.UTC().Format("2006-01-02T15:04:05Z"), one.Name)
	}

	for _, one := range listing.Files {
		fmt.Fprintf(out, "%s\tfile\t%d\t%s\t%s\n",
			one.Id, one.Size, one.UpdatedAt.UTC().Format("2006-01-02T15:04:05Z"), one.Name)
	}

	return nil
}

func (g *globals) filesPut(ctx context.Context, file, to, name string) error {
	if file == "" {
		return &config.UsageError{
			Message: "`pea files put` takes its bytes from --file FILE, or from stdin with -.",
		}
	}

	if name == "" {
		if file == "-" {
			return &config.UsageError{
				Message: "`pea files put --file -` has no name to take: --name says what to call it.",
			}
		}
		name = baseName(file)
	}

	content, err := readContent(g.in(), file)
	if err != nil {
		return err
	}

	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	folder, err := resolveFolder(ctx, c, to)
	if err != nil {
		return err
	}

	params := &api.UploadFileParams{Name: name, Folder: folder}

	// The body is the file and `Content-Type` is what it is stored as. pea does
	// not guess one from the name: a `.png` holding something else is a thing
	// that exists, and the guess would be pea's word for what the bytes are.
	resp, err := c.UploadFileWithBodyWithResponse(
		ctx, params, "application/octet-stream", bytes.NewReader(content))
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

	// The id alone, so that `id=$(pea files put --file -)` is the whole of what
	// a script has to do to hold on to what it wrote.
	fmt.Fprintln(g.out(), resp.JSON201.Id)
	fmt.Fprintf(g.msg(), "pea: stored %d byte(s) as %s.\n", resp.JSON201.Size, resp.JSON201.Name)

	return nil
}

func (g *globals) filesGet(ctx context.Context, what, out string) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	file, err := resolveFile(ctx, c, what)
	if err != nil {
		return err
	}

	if g.json {
		return render.JSON(g.out(), file)
	}

	resp, err := c.DownloadFileWithResponse(ctx, file.Id)
	if err != nil {
		return client.Transport(err)
	}
	if err := client.CheckBytes(resp.HTTPResponse, resp.Body); err != nil {
		return err
	}

	if out == "-" {
		_, err := g.out().Write(resp.Body)
		return err
	}

	if out == "" {
		out = file.Name
	}

	// Nothing in pea overwrites a file it did not make. A download that
	// silently replaced something in the working directory would be the one
	// destructive thing a read can do.
	handle, err := os.OpenFile(out, os.O_WRONLY|os.O_CREATE|os.O_EXCL, 0o600)
	if err != nil {
		return &config.UsageError{
			Message: fmt.Sprintf("cannot write %s: %v. --out says where the bytes go.", out, err),
		}
	}
	defer handle.Close()

	if _, err := handle.Write(resp.Body); err != nil {
		return err
	}

	fmt.Fprintf(g.msg(), "pea: wrote %d byte(s) to %s.\n", len(resp.Body), out)

	return nil
}

func (g *globals) filesMkdir(ctx context.Context, path string, parents bool) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	segments := split(path)
	if len(segments) == 0 {
		return &config.UsageError{Message: "`pea files mkdir` takes the path of a folder to make."}
	}

	var parent *openapi_types.UUID

	for index, segment := range segments {
		last := index == len(segments)-1

		found, err := lookIn(ctx, c, parent, segment)
		if err != nil {
			return err
		}

		if found != nil {
			if last {
				return &Refused{
					code: exit.Conflict,
					what: fmt.Sprintf("something called %q is already there.", segment),
				}
			}
			if found.folder == nil {
				return notAFolder(segment)
			}
			parent = found.folder
			continue
		}

		if !last && !parents {
			return &Refused{
				code: exit.NotFound,
				what: fmt.Sprintf("%q is not a folder here; --parents makes the ones above it.", segment),
			}
		}

		made, err := makeFolder(ctx, c, segment, parent)
		if err != nil {
			return err
		}

		if last {
			if g.json {
				return render.JSON(g.out(), made)
			}
			fmt.Fprintln(g.out(), made.Id)
			fmt.Fprintf(g.msg(), "pea: made %s.\n", made.Name)
			return nil
		}

		id := made.Id
		parent = &id
	}

	return nil
}

func (g *globals) filesMove(ctx context.Context, what, dest, ifMatch string) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	thing, err := resolve(ctx, c, what)
	if err != nil {
		return err
	}

	folder, name, err := destination(ctx, c, dest, thing.name)
	if err != nil {
		return err
	}

	if ifMatch == "" {
		ifMatch = client.EntityTag(thing.updatedAt)
	}

	if thing.folder != nil {
		resp, err := c.ChangeFolderWithResponse(
			ctx,
			*thing.folder,
			&api.ChangeFolderParams{IfMatch: ifMatch},
			api.ChangeFolderJSONRequestBody{Name: &name, Folder: folder})
		if err != nil {
			return client.Transport(err)
		}
		if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
			return err
		}
		if resp.JSON200 == nil {
			return answeredNothing(resp.HTTPResponse.Status)
		}
		return g.moved(resp.JSON200, resp.JSON200.Name)
	}

	resp, err := c.ChangeFileWithResponse(
		ctx,
		*thing.file,
		&api.ChangeFileParams{IfMatch: ifMatch},
		api.ChangeFileJSONRequestBody{Name: &name, Folder: folder})
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return err
	}
	if resp.JSON200 == nil {
		return answeredNothing(resp.HTTPResponse.Status)
	}

	return g.moved(resp.JSON200, resp.JSON200.Name)
}

func (g *globals) moved(body any, name string) error {
	if g.json {
		return render.JSON(g.out(), body)
	}

	fmt.Fprintf(g.msg(), "pea: it is now %s.\n", name)

	return nil
}

func (g *globals) filesRemove(ctx context.Context, what, ifMatch string) error {
	_, c, err := g.asSomebody()
	if err != nil {
		return err
	}

	thing, err := resolve(ctx, c, what)
	if err != nil {
		return err
	}

	if ifMatch == "" {
		ifMatch = client.EntityTag(thing.updatedAt)
	}

	if thing.folder != nil {
		resp, err := c.DiscardFolderWithResponse(
			ctx, *thing.folder, &api.DiscardFolderParams{IfMatch: ifMatch})
		if err != nil {
			return client.Transport(err)
		}
		if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
			return err
		}

		fmt.Fprintf(g.msg(),
			"pea: %s and everything in it are in the Trash. `pea trash restore files %s` brings it back.\n",
			thing.name, *thing.folder)

		return nil
	}

	resp, err := c.DiscardFileWithResponse(ctx, *thing.file, &api.DiscardFileParams{IfMatch: ifMatch})
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return err
	}

	fmt.Fprintf(g.msg(),
		"pea: %s is in the Trash. `pea trash restore files %s` brings it back.\n",
		thing.name, *thing.file)

	return nil
}

// found is one thing in the tree, whichever kind it is. Exactly one of folder
// and file is set, which is how every verb below tells a rename of a folder
// from a rename of a file without asking twice.
type found struct {
	folder    *openapi_types.UUID
	file      *openapi_types.UUID
	name      string
	updatedAt time.Time
}

// Refused is pea's own refusal, for what it works out before it sends anything:
// a path segment that names nothing, or a file where a folder was wanted. It
// carries the exit code the same answer from the instance would have had, so a
// script cannot tell whether pea or the instance noticed.
type Refused struct {
	code int
	what string
}

func (r *Refused) Error() string { return r.what }

func (r *Refused) ExitCode() int { return r.code }

// readFolder is one listing: what is in a folder, or in the top of the tree.
func readFolder(
	ctx context.Context, c *client.Client, folder *openapi_types.UUID,
) (*api.FilesResponse, error) {
	resp, err := c.ReadFolderWithResponse(ctx, &api.ReadFolderParams{Folder: folder})
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

func makeFolder(
	ctx context.Context, c *client.Client, name string, parent *openapi_types.UUID,
) (*api.FolderResponse, error) {
	resp, err := c.MakeFolderWithResponse(
		ctx, api.MakeFolderJSONRequestBody{Name: &name, Parent: parent})
	if err != nil {
		return nil, client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return nil, err
	}
	if resp.JSON201 == nil {
		return nil, answeredNothing(resp.HTTPResponse.Status)
	}

	return resp.JSON201, nil
}

// lookIn finds one name in one folder, or nothing where it is not there.
//
// Names in a folder are one each whatever their capitals (docs/api.md), so the
// comparison is the instance's rule and not Go's: a path typed in the wrong
// case finds the thing it meant, exactly as it would on the machine the person
// is typing at.
func lookIn(
	ctx context.Context, c *client.Client, folder *openapi_types.UUID, name string,
) (*found, error) {
	listing, err := readFolder(ctx, c, folder)
	if err != nil {
		return nil, err
	}

	for _, one := range listing.Folders {
		if strings.EqualFold(one.Name, name) {
			id := one.Id
			return &found{folder: &id, name: one.Name, updatedAt: one.UpdatedAt}, nil
		}
	}

	for _, one := range listing.Files {
		if strings.EqualFold(one.Name, name) {
			id := one.Id
			return &found{file: &id, name: one.Name, updatedAt: one.UpdatedAt}, nil
		}
	}

	return nil, nil
}

// resolve turns what the caller typed into one thing on the instance.
//
// A path is walked a segment at a time, so the wire carries ids and nothing
// server-side has to keep a second name for anything. An id is accepted
// wherever a path is, because that is what every other verb prints — and an id
// is tried first, so a folder somebody called `0199f0c4-…` does not shadow one.
func resolve(ctx context.Context, c *client.Client, what string) (*found, error) {
	if id, err := uuid.Parse(strings.TrimSpace(what)); err == nil {
		return byID(ctx, c, id)
	}

	segments := split(what)
	if len(segments) == 0 {
		return nil, &config.UsageError{
			Message: "the top of the tree is not something to move, rename or delete.",
		}
	}

	var folder *openapi_types.UUID

	for index, segment := range segments {
		one, err := lookIn(ctx, c, folder, segment)
		if err != nil {
			return nil, err
		}
		if one == nil {
			return nil, nothingCalled(segment)
		}
		if index == len(segments)-1 {
			return one, nil
		}
		if one.folder == nil {
			return nil, notAFolder(segment)
		}
		folder = one.folder
	}

	return nil, nothingCalled(what)
}

// byID asks the instance what an id is: a file if it answers as one, and
// otherwise a folder, which is the one listing that can say a folder exists.
func byID(ctx context.Context, c *client.Client, id openapi_types.UUID) (*found, error) {
	file, err := c.ReadFileWithResponse(ctx, id)
	if err != nil {
		return nil, client.Transport(err)
	}

	if file.JSON200 != nil {
		return &found{file: &id, name: file.JSON200.Name, updatedAt: file.JSON200.UpdatedAt}, nil
	}

	// Not a file. It may still be a folder, and the way to find out is to read
	// it: a folder's own listing answers its chain, whose last step is itself.
	listing, err := readFolder(ctx, c, &id)
	if err != nil {
		// The file's refusal is the better one to report: it is the one that
		// says `deleted` where the thing is in the Trash, and the listing can
		// only ever say `not-found`.
		if problem := client.Check(file.HTTPResponse, file.Body); problem != nil {
			return nil, problem
		}
		return nil, err
	}

	if len(listing.Chain) == 0 {
		return nil, nothingCalled(id.String())
	}

	itself := listing.Chain[len(listing.Chain)-1]

	return &found{folder: &itself.Id, name: itself.Name, updatedAt: itself.UpdatedAt}, nil
}

// resolveFolder is the folder a path names, or nothing for the top of the tree
// — which is what an empty path and `/` both mean.
func resolveFolder(
	ctx context.Context, c *client.Client, where string,
) (*openapi_types.UUID, error) {
	if len(split(where)) == 0 {
		return nil, nil
	}

	one, err := resolve(ctx, c, where)
	if err != nil {
		return nil, err
	}

	if one.folder == nil {
		return nil, notAFolder(one.name)
	}

	return one.folder, nil
}

// resolveFile is the file a path or an id names, with its metadata.
func resolveFile(ctx context.Context, c *client.Client, what string) (*api.FileResponse, error) {
	one, err := resolve(ctx, c, what)
	if err != nil {
		return nil, err
	}

	if one.file == nil {
		return nil, &Refused{
			code: exit.Refused,
			what: fmt.Sprintf("%q is a folder, and a folder has no bytes.", one.name),
		}
	}

	resp, err := c.ReadFileWithResponse(ctx, *one.file)
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

// destination reads a `mv` target: where the thing goes, and what it is called
// when it lands.
//
// A target that names an existing folder means "into it, under the name it
// already has", which is what `mv` means everywhere else. Anything else is the
// place and the name together, and the place has to exist — `mv` has never made
// directories.
func destination(
	ctx context.Context, c *client.Client, dest, itsOwn string,
) (*openapi_types.UUID, string, error) {
	segments := split(dest)

	if len(segments) == 0 {
		// `/` is the top of the tree: moved there, keeping its name.
		return nil, itsOwn, nil
	}

	var parent *openapi_types.UUID

	for index, segment := range segments {
		last := index == len(segments)-1

		one, err := lookIn(ctx, c, parent, segment)
		if err != nil {
			return nil, "", err
		}

		if one == nil {
			if last {
				// The last segment names nothing, so it is the new name.
				return parent, segment, nil
			}
			return nil, "", nothingCalled(segment)
		}

		if one.folder == nil {
			if last {
				// Something is already there and it is a file. The instance
				// refuses this as `conflict`; sending it is what makes the
				// sentence the instance's rather than pea's.
				return parent, segment, nil
			}
			return nil, "", notAFolder(segment)
		}

		if last {
			return one.folder, itsOwn, nil
		}

		parent = one.folder
	}

	return parent, itsOwn, nil
}

func notAFolder(segment string) error {
	return &Refused{
		code: exit.Refused,
		what: fmt.Sprintf("%q is a file, and a file has nothing in it.", segment),
	}
}

func nothingCalled(segment string) error {
	return &Refused{
		code: exit.NotFound,
		what: fmt.Sprintf("nothing here is called %q.", segment),
	}
}

// split turns a path into its segments. Leading, trailing and doubled
// separators are nothing, so `/Reisen/`, `Reisen` and `//Reisen` are one path.
func split(path string) []string {
	var segments []string

	for _, segment := range strings.Split(path, "/") {
		if segment != "" {
			segments = append(segments, segment)
		}
	}

	return segments
}

func first(args []string) string {
	if len(args) == 0 {
		return ""
	}
	return args[0]
}

// baseName is the name a file has on this machine, without the directories in
// front of it. It is not filepath.Base, because what is being cut is a path the
// caller typed at whichever shell they are at, and what comes out is a name the
// instance will hold to its own rules.
func baseName(path string) string {
	cleaned := strings.TrimRight(path, "/\\")

	if at := strings.LastIndexAny(cleaned, "/\\"); at >= 0 {
		return cleaned[at+1:]
	}

	return cleaned
}
