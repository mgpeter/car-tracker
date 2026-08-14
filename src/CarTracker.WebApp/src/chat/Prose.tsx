import Markdown, { type Components } from 'react-markdown'
import remarkGfm from 'remark-gfm'

/**
 * A chat message, rendered as the Markdown it is.
 *
 * Models write Markdown — `**BT53 AKJ**`, bullet lists, and a pipe table when asked for sixteen fuel fills.
 * The panel used to put the raw string in a `<p>` and let `white-space: pre-wrap` do the rest, so the
 * asterisks showed and the table arrived as a wall of pipes.
 *
 * **`react-markdown` builds React elements and never touches `innerHTML`**, which is what makes it usable
 * here: nothing else in this app injects HTML from data, and the production CSP is `default-src 'self';
 * style-src 'self'` with no `unsafe-inline` (`plugins/theme-csp.ts`), so anything injecting markup or styles
 * would fail in production only — the worst place to find out. Raw HTML in the source is inert without
 * `rehype-raw`, which is deliberately not installed.
 *
 * Used for the owner's own messages too. They are usually plain, but a pasted workbook table is exactly the
 * thing this renders well.
 */
export function Prose({ children }: { children: string }) {
  return (
    <Markdown remarkPlugins={[remarkGfm]} components={COMPONENTS}>
      {children}
    </Markdown>
  )
}

/**
 * Every element gets a house class rather than a browser default.
 *
 * Headings are levelled rather than sized: a chat bubble has no document outline, and an `<h1>` inside a
 * transcript would out-shout the panel's own title. They render as bold lines and keep their semantics for a
 * screen reader.
 */
const COMPONENTS: Components = {
  p: ({ children }) => <p className="pr-p">{children}</p>,
  h1: ({ children }) => <h4 className="pr-h">{children}</h4>,
  h2: ({ children }) => <h4 className="pr-h">{children}</h4>,
  h3: ({ children }) => <h4 className="pr-h">{children}</h4>,
  h4: ({ children }) => <h4 className="pr-h">{children}</h4>,
  h5: ({ children }) => <h4 className="pr-h">{children}</h4>,
  h6: ({ children }) => <h4 className="pr-h">{children}</h4>,
  ul: ({ children }) => <ul className="pr-list">{children}</ul>,
  ol: ({ children }) => <ol className="pr-list pr-num">{children}</ol>,
  li: ({ children }) => <li>{children}</li>,
  code: ({ children }) => <code className="pr-code">{children}</code>,
  pre: ({ children }) => <pre className="pr-pre">{children}</pre>,
  blockquote: ({ children }) => <blockquote className="pr-quote">{children}</blockquote>,
  hr: () => <hr className="pr-rule" />,

  // Wide content scrolls inside its own box and the panel never scrolls sideways — the rule `DataTable`
  // already follows. A six-column fill table in a 440px dock has to scroll somewhere; better here than
  // taking the whole conversation with it.
  table: ({ children }) => (
    <div className="pr-tablebox">
      <table className="pr-table">{children}</table>
    </div>
  ),

  // An image is rendered as its own description. `img-src 'self' data: blob:` would block a remote one in
  // production regardless, so the choice is between a broken-image icon and the words the model used —
  // and `unwrapDisallowed` is not the third option it looks like: alt text is an attribute, not a child, so
  // unwrapping an image yields nothing at all.
  img: ({ alt }) => <span className="pr-noimg">{alt}</span>,

  // Model output is not the owner's writing. `noopener` because a link that can reach back into this tab is
  // one that can navigate the app away from a half-finished draft, and `nofollow` because nothing here
  // endorses where the model points.
  a: ({ href, children }) => (
    <a className="pr-link" href={href} target="_blank" rel="noopener noreferrer nofollow">
      {children}
    </a>
  ),
}
