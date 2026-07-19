import * as React from 'react'

import { cn } from '@/lib/utils'
import { fieldStateClasses } from './field'

function Textarea({ className, ...props }: React.ComponentProps<'textarea'>) {
  return (
    <textarea
      data-slot="textarea"
      className={cn(
        'min-h-16 w-full resize-y rounded-md border px-2.5 py-1.5 text-[13px] leading-relaxed outline-none transition-colors',
        fieldStateClasses,
        className,
      )}
      {...props}
    />
  )
}

export { Textarea }
