/** Layout-free stand-in for autosave tests. The actual editor is tested in Chromium. */
export default function StandInPageEditor({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  // eslint-disable-next-line no-restricted-syntax
  return <textarea aria-label="The page" value={value} onChange={event => onChange(event.target.value)} />;
}
