import { DocumentOutline } from "./DocumentOutline";
import { CrepeBuilder } from "@milkdown/crepe/builder";
import { blockEdit } from "@milkdown/crepe/feature/block-edit";
import { cursor } from "@milkdown/crepe/feature/cursor";
import { linkTooltip } from "@milkdown/crepe/feature/link-tooltip";
import { listItem } from "@milkdown/crepe/feature/list-item";
import { placeholder } from "@milkdown/crepe/feature/placeholder";
import { table } from "@milkdown/crepe/feature/table";
import { toolbar } from "@milkdown/crepe/feature/toolbar";
import { toggleLinkCommand } from "@milkdown/kit/component/link-tooltip";
import { useNavigate } from "react-router";
import { BoldIcon, ItalicIcon, CodeIcon, LinkIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { editorViewCtx, editorViewOptionsCtx, serializerCtx, remarkPluginsCtx } from "@milkdown/kit/core";
import { imageSchema, linkSchema, toggleStrongCommand, toggleEmphasisCommand, toggleInlineCodeCommand, setBlockTypeCommand, paragraphSchema, headingSchema } from "@milkdown/kit/preset/commonmark";
import { Plugin } from "@milkdown/kit/prose/state";
import { $prose, callCommand, insert, replaceAll } from "@milkdown/kit/utils";
import { Milkdown, MilkdownProvider, useEditor } from "@milkdown/react";
import remarkBreaks from "remark-breaks";
import { useEffect, useMemo, useRef, useState } from "react";
import { admitUrl, fileHref, insidePath } from "@/shared/links";
import type { EditorHandle } from "@/shared/Editor";
import { PageLinkPicker, type LinkablePage } from "./PageLinkPicker";
import { useMedia } from "./useMedia";
import "@milkdown/crepe/theme/common/prosemirror.css";
import "@milkdown/crepe/theme/common/reset.css";
import "@milkdown/crepe/theme/common/block-edit.css";
import "@milkdown/crepe/theme/common/cursor.css";
import "@milkdown/crepe/theme/common/link-tooltip.css";
import "@milkdown/crepe/theme/common/list-item.css";
import "@milkdown/crepe/theme/common/placeholder.css";
import "@milkdown/crepe/theme/common/toolbar.css";
import "@milkdown/crepe/theme/common/table.css";
import "@milkdown/crepe/theme/frame.css";
import "./page-editor.css";

type Props = { value: string; onChange: (value: string) => void; pages: LinkablePage[] };

/** One mounted document, including while saving or changing the page layout. */
export default function PageEditor(props: Props) {
  return <MilkdownProvider><Document {...props} /></MilkdownProvider>;
}

function Document({ value, onChange, pages }: Props) {
  const navigate = useNavigate();
  const documentRoot = useRef<HTMLDivElement>(null);
  const formatting = useRef<HTMLDivElement>(null);
  const [block, setBlock] = useState("paragraph");
  const [failedImages, setFailedImages] = useState<Record<string, string>>({});
  const latest = useRef({ value, onChange });
  const output = useRef(value);
  const applied = useRef(value);
  const replacing = useRef(false);
  const ready = useRef(false);
  useEffect(() => { latest.current = { value, onChange }; });

  const { loading, get } = useEditor(root => {
    const crepe = new CrepeBuilder({
      root,
      defaultValue: latest.current.value,
    }).addFeature(blockEdit).addFeature(cursor).addFeature(listItem)
      .addFeature(linkTooltip).addFeature(table).addFeature(toolbar)
      .addFeature(placeholder, { text: "Start writing… Type / for lists, tables and more.", mode: "doc" });
    crepe.editor.config(ctx => {
      ctx.update(editorViewOptionsCtx, previous => ({ ...previous, attributes: { role: "textbox", "aria-label": "The page", "aria-multiline": "true", spellcheck: "true" } }));
      // Keep the existing Knowledge convention: a single newline is a break.
      ctx.update(remarkPluginsCtx, plugins => [...plugins, { plugin: remarkBreaks, options: {} }]);
      // Crepe's optional image-block format uses Markdown alt text for image
      // sizing. Keep ordinary Markdown images so existing alt/title survive.
      ctx.update(imageSchema.key, previous => context => ({
        ...previous(context),
        parseDOM: [{ tag: "img[data-markdown-src]", getAttrs: dom => ({ src: dom.getAttribute("data-markdown-src") ?? "", alt: dom.getAttribute("alt") ?? "", title: dom.getAttribute("title") ?? "" }) }, ...(previous(context).parseDOM ?? [])],
        toDOM: node => ["img", { ...node.attrs, "data-markdown-src": node.attrs.src, src: fileHref(node.attrs.src) ?? imageUrl(node.attrs.src), loading: "lazy" }],
      }));
      ctx.update(linkSchema.key, previous => context => ({
        ...previous(context),
        parseDOM: [{ tag: "a[data-markdown-href]", getAttrs: dom => ({ href: dom.getAttribute("data-markdown-href") ?? "", title: dom.getAttribute("title") }) }, ...(previous(context).parseDOM ?? [])],
        toDOM: mark => {
          const href = insidePath(mark.attrs.href) ?? admitUrl(mark.attrs.href, "href", { type: "element", tagName: "a", properties: {}, children: [] });
          return ["a", { ...mark.attrs, "data-markdown-href": mark.attrs.href, href, target: href?.startsWith("/knowledge/") ? undefined : "_blank", rel: "noopener noreferrer" }, 0];
        },
      }));
    });
    // Persist each transaction locally immediately. Milkdown's normal Markdown
    // listener is debounced, which could drop the final keystroke on navigation.
    crepe.editor.use($prose(ctx => new Plugin({
      view: () => ({ update: (view, previous) => {
        const parent = view.state.selection.$from.parent;
        setBlock(parent.type.name === "heading" ? String(parent.attrs.level) : "paragraph");
        if (!ready.current || replacing.current || view.state.doc.eq(previous.doc)) return;
        const markdown = ctx.get(serializerCtx)(view.state.doc);
        if (markdown === output.current) return;
        output.current = markdown;
        applied.current = markdown;
        latest.current.onChange(markdown);
      } }),
    })));
    crepe.on(listener => listener.mounted(() => {
      output.current = crepe.getMarkdown();
      const parent = crepe.editor.action(ctx => ctx.get(editorViewCtx).state.selection.$from.parent);
      setBlock(parent.type.name === "heading" ? String(parent.attrs.level) : "paragraph");
      ready.current = true;
    }).destroy(() => { ready.current = false; }));
    return crepe;
  }, []);

  const editor = loading ? undefined : get();
  const handle = useMemo<EditorHandle | null>(() => editor ? {
    apply: command => {
      const selected = editor.action(ctx => {
        const { doc, selection } = ctx.get(editorViewCtx).state;
        return doc.textBetween(selection.from, selection.to, "\n");
      });
      editor.action(insert(command({ text: selected, from: 0, to: selected.length }).text, true));
      editor.action(ctx => ctx.get(editorViewCtx).focus());
    },
    focus: () => editor.action(ctx => ctx.get(editorViewCtx).focus()),
  } : null, [editor]);

  useEffect(() => {
    if (!editor || value === applied.current) return;
    replacing.current = true;
    try {
      editor.action(replaceAll(value));
      output.current = editor.action(ctx => ctx.get(serializerCtx)(ctx.get(editorViewCtx).state.doc));
      applied.current = value;
    } finally { replacing.current = false; }
  }, [value, editor]);

  const media = useMedia(handle, true);
  return <div className="knowledge-document" onErrorCapture={event => {
    if (!(event.target instanceof HTMLImageElement)) return;
    const source = event.target.dataset.markdownSrc;
    const alt = event.target.alt || "untitled image";
    if (source) setFailedImages(previous => ({ ...previous, [source]: alt }));
  }} onPasteCapture={media.onPaste} onDropCapture={media.onDrop} onDragOver={media.onDragOver}
    onClickCapture={event => {
      const anchor = (event.target as Element).closest<HTMLAnchorElement>("a");
      if (!anchor) return;
      const tooltip = anchor.classList.contains("link-display");
      if (!tooltip && !event.metaKey && !event.ctrlKey) return;
      const raw = tooltip ? anchor.textContent ?? "" : anchor.getAttribute("href") ?? "";
      const href = insidePath(raw) ?? fileHref(raw) ?? (raw.startsWith("/knowledge/") || /^\/api\/files\/[0-9a-f-]+\/content$/i.test(raw) ? raw : admitUrl(raw, "href", { type: "element", tagName: "a", properties: {}, children: [] }));
      event.preventDefault();
      if (href?.startsWith("/knowledge/")) void navigate(href);
      else if (href) window.open(href, "_blank", "noopener,noreferrer");
    }}>
    <DocumentOutline documentRoot={documentRoot} toolbar={formatting} ready={!loading} />
    <div ref={formatting} aria-label="Document formatting" role="group" className="sticky top-0 z-10 mb-2 flex flex-wrap items-center gap-1 border-b bg-background/95 py-2">
      <select name="paragraph-style" aria-label="Paragraph style" value={block} disabled={!editor} className="rounded-md bg-background px-2 py-1.5 text-sm" onChange={event => {
        const level = event.target.value;
        editor?.action(ctx => callCommand(setBlockTypeCommand.key, { nodeType: level === "paragraph" ? paragraphSchema.type(ctx) : headingSchema.type(ctx), attrs: level === "paragraph" ? undefined : { level: Number(level) } })(ctx));
        handle?.focus();
      }}>
        <option value="paragraph">Paragraph</option>
        {[1, 2, 3, 4, 5, 6].map(level => <option key={level} value={level}>Heading {level}</option>)}
      </select>
      <Button size="icon-sm" variant="ghost" aria-label="Bold" title="Bold (⌘/Ctrl+B)" disabled={!editor} onClick={() => { editor?.action(callCommand(toggleStrongCommand.key)); handle?.focus(); }}><BoldIcon /></Button>
      <Button size="icon-sm" variant="ghost" aria-label="Italic" title="Italic (⌘/Ctrl+I)" disabled={!editor} onClick={() => { editor?.action(callCommand(toggleEmphasisCommand.key)); handle?.focus(); }}><ItalicIcon /></Button>
      <Button size="icon-sm" variant="ghost" aria-label="Inline code" title="Inline code" disabled={!editor} onClick={() => { editor?.action(callCommand(toggleInlineCodeCommand.key)); handle?.focus(); }}><CodeIcon /></Button>
      <Button size="icon-sm" variant="ghost" aria-label="Insert link" title="Insert link" disabled={!editor} onClick={() => editor?.action(callCommand(toggleLinkCommand.key))}><LinkIcon /></Button>
      <PageLinkPicker pages={pages} disabled={!handle} onPick={page => handle?.apply(at => ({ text: `[${(at.text || page.title).replace(/[\\[\]]/g, "\\$&")}](page:${page.id})`, from: 0, to: 0 }))} />
      {media.controls}
    </div>
    {media.feedback}
    {Object.entries(failedImages).filter(([source]) => value.includes(source)).map(([source, alt]) =>
      <p key={source} role="status" className="text-sm text-muted-foreground">Image unavailable: {alt}. It may have been deleted or access may be restricted.</p>)}
    {loading && <p role="status" className="text-sm text-muted-foreground">Loading document…</p>}
    <div ref={documentRoot}><Milkdown /></div>
  </div>;
}

function imageUrl(url: string) {
  try { return ["https:", "http:"].includes(new URL(url).protocol) ? url : ""; }
  catch { return ""; }
}
