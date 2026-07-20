import * as React from 'react'

import { cn } from '@/lib/utils'
import { fieldStateClasses } from './field'

function Input({ className, type, ...props }: React.ComponentProps<'input'>) {
  return (
    <input
      type={type}
      data-slot="input"
      className={cn(
        'h-8 w-full min-w-0 rounded-md border px-2.5 text-[13px] outline-none transition-colors',
        fieldStateClasses,
        className,
      )}
      {...props}
    />
  )
}

export { Input }
