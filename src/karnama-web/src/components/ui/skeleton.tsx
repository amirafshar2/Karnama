// ⚠ TEMPORARY STAND-IN: shadcn/ui (new-york-v4, also published on 21st.dev by @shadcn), RTL-adapted.
// Replace with the chosen 21st.dev component once TWENTYFIRST_API_KEY is set — see docs/21st-components.md.
import { cn } from "@/lib/utils"

function Skeleton({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="skeleton"
      className={cn("animate-pulse rounded-md bg-accent", className)}
      {...props}
    />
  )
}

export { Skeleton }
