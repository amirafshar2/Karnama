"use client"
// ⚠ TEMPORARY STAND-IN: shadcn/ui (new-york-v4, also published on 21st.dev by @shadcn), RTL-adapted.
// Replace with the chosen 21st.dev component once TWENTYFIRST_API_KEY is set — see docs/21st-components.md.

import * as React from "react"
import { cn } from "@/lib/utils"
import { Label as LabelPrimitive } from "radix-ui"

function Label({
  className,
  ...props
}: React.ComponentProps<typeof LabelPrimitive.Root>) {
  return (
    <LabelPrimitive.Root
      data-slot="label"
      className={cn(
        "flex items-center gap-2 text-sm leading-none font-medium select-none group-data-[disabled=true]:pointer-events-none group-data-[disabled=true]:opacity-50 peer-disabled:cursor-not-allowed peer-disabled:opacity-50",
        className
      )}
      {...props}
    />
  )
}

export { Label }
