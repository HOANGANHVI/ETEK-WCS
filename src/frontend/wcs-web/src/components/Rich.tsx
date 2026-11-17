import { Fragment, type ReactNode } from 'react'

/**
 * Renders a translated sentence that contains inline markup. The translation keeps `{name}` placeholders and the caller supplies
 * the nodes, so word order can differ between languages without concatenating translated fragments.
 */
export function Rich({ text, parts }: { text: string; parts: Record<string, ReactNode> }) {
  const bits = text.split(/(\{\w+\})/g)
  return (
    <>
      {bits.map((b, i) => {
        const m = /^\{(\w+)\}$/.exec(b)
        return m && m[1] in parts ? <Fragment key={i}>{parts[m[1]]}</Fragment> : b
      })}
    </>
  )
}
