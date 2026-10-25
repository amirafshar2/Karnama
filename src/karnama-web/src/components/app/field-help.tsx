/** One plain-language line under a field (docs/PROMPT2.md rule 0.2). */
export function FieldHelp({ id, children }: { id: string; children: React.ReactNode }) {
  return (
    <p id={id} className="text-sm text-muted-foreground">
      {children}
    </p>
  );
}
