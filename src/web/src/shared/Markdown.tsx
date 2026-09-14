import { Children, isValidElement, type ComponentProps, type ReactNode } from "react";
import ReactMarkdown, { type Components } from "react-markdown";
import { Link } from "react-router";
import remarkBreaks from "remark-breaks";
import remarkGfm from "remark-gfm";
import { cn } from "@/lib/utils";
import { admitUrl, insidePath } from "./links";

/**
 * The Markdown pipeline (ADR 0001): `react-markdown` with `remark-gfm`, parsed
 * to a component tree, with raw HTML skipped — never interpreted, never set as
 * innerHTML — because what it renders may have been written by an agent
 * quoting something nobody vetted.
 *
 * `remark-breaks` is the third plugin and the one that is not CommonMark: a
 * single newline is a line break rather than a space, because this text is
 * typed into an editor by somebody who presses Enter and expects a break.
 *
 * Links are foreign links: exactly `http`, `https` and `mailto`, and anything
 * else stays text (`shared/links.ts`). An address inside this workspace is
 * followed rather than opened — no new tab, no `noopener`, and the frame never
 * remounted — and PERSONAL-E7 is what gives a page one.
 */
const components: Components = {
  h1: ({ className, ...props }) => <h2 className={cn("mt-6 mb-2 text-base font-semibold first:mt-0", className)} {...props} />,
  h2: ({ className, ...props }) => <h3 className={cn("mt-5 mb-2 text-sm font-semibold first:mt-0", className)} {...props} />,
  h3: ({ className, ...props }) => <h4 className={cn("mt-4 mb-1 text-sm font-medium first:mt-0", className)} {...props} />,
  p: ({ className, ...props }) => <p className={cn("my-2 leading-6 first:mt-0 last:mb-0", className)} {...props} />,
  a: ({ className, href, ...props }) => {
    const style = cn("text-brand underline-offset-4 hover:underline", className);
    const inside = insidePath(href);

    if (inside !== undefined) {
      return <Link to={inside} className={style} {...props} />;
    }

    return href === undefined ? (
      <span className={cn("text-muted-foreground", className)} {...props} />
    ) : (
      <a href={href} target="_blank" rel="noopener noreferrer" className={style} {...props} />
    );
  },
  ul: ({ className, ...props }) => <ul className={cn("my-2 list-disc pl-5 marker:text-muted-foreground", className)} {...props} />,
  ol: ({ className, ...props }) => <ol className={cn("my-2 list-decimal pl-5 marker:text-muted-foreground", className)} {...props} />,
  li: ({ className, ...props }) => <li className={cn("my-0.5 leading-6", className)} {...props} />,
  blockquote: ({ className, ...props }) => (
    <blockquote className={cn("my-2 border-l-2 pl-3 text-muted-foreground", className)} {...props} />
  ),
  hr: ({ className, ...props }) => <hr className={cn("my-4", className)} {...props} />,
  code: ({ className, children, ...props }) => {
    // A fenced block arrives as `code` inside `pre` with a language class; an
    // inline span arrives bare. Neither is highlighted: nothing tokenizes code
    // here, and the fence's own word says which language it is.
    const fenced = /language-/.test(className ?? "");

    return (
      <code
        className={cn(
          "font-mono text-[0.85em]",
          !fenced && "rounded-sm bg-muted px-1 py-0.5",
          className,
        )}
        {...props}
      >
        {children}
      </code>
    );
  },
  pre: ({ className, children, ...props }) => {
    const language = languageOf(children);

    return (
      <div className="my-3 overflow-hidden rounded-md border bg-muted">
        {language !== undefined && (
          <div className="border-b px-3 py-1 font-mono text-[0.7rem] text-muted-foreground">{language}</div>
        )}
        <pre className={cn("overflow-x-auto p-3 text-xs leading-5", className)} {...props}>
          {children}
        </pre>
      </div>
    );
  },
  table: ({ className, ...props }) => (
    <div className="my-3 overflow-x-auto">
      <table className={cn("w-full border-collapse text-sm", className)} {...props} />
    </div>
  ),
  th: ({ className, ...props }) => (
    <th className={cn("border-b px-2 py-1 text-left font-medium", className)} {...props} />
  ),
  td: ({ className, ...props }) => <td className={cn("border-b px-2 py-1 align-top", className)} {...props} />,
  input: ({ className, ...props }) =>
    props.type === "checkbox" ? (
      // The box a task list in the description draws. It is disabled — the
      // text is the truth, and it is edited as text — so it is not a form
      // field anybody fills in, and it needs no name the browser could learn.
      // eslint-disable-next-line no-restricted-syntax
      <input className={cn("mr-1.5 align-middle accent-brand", className)} {...props} disabled />
    ) : null,
};

/**
 * The language a fence named, from the `language-…` class `react-markdown` puts
 * on the `code` inside the `pre`. Nothing acts on it but the label: the block
 * is not highlighted.
 */
function languageOf(children: ReactNode): string | undefined {
  const first = Children.toArray(children)[0];

  if (!isValidElement<{ className?: string }>(first)) {
    return undefined;
  }

  return /language-([\w+#-]+)/.exec(first.props.className ?? "")?.[1];
}

export function Markdown({ children, className }: { children: string; className?: string }) {
  return (
    <div className={cn("text-sm", className)}>
      <ReactMarkdown remarkPlugins={[remarkGfm, remarkBreaks]} skipHtml urlTransform={admitUrl} components={components}>
        {children}
      </ReactMarkdown>
    </div>
  );
}

export type MarkdownProps = ComponentProps<typeof Markdown>;
