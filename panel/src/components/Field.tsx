import { useId, type InputHTMLAttributes, type ReactNode, type SelectHTMLAttributes, type TextareaHTMLAttributes } from 'react'

interface FieldShellProps {
  id: string
  label: ReactNode
  hint?: ReactNode
  error?: string
  className?: string
  children: ReactNode
}

function describedBy(id: string, hint?: ReactNode, error?: string): string | undefined {
  const ids = [hint ? `${id}-hint` : '', error ? `${id}-error` : ''].filter(Boolean)
  return ids.length ? ids.join(' ') : undefined
}

function FieldShell({ id, label, hint, error, className, children }: FieldShellProps) {
  return (
    <div className={`field${error ? ' field-invalid' : ''}${className ? ` ${className}` : ''}`}>
      <label htmlFor={id}>{label}</label>
      {children}
      {hint && (
        <p className="field-hint" id={`${id}-hint`}>
          {hint}
        </p>
      )}
      {error && (
        <p className="field-error" id={`${id}-error`}>
          {error}
        </p>
      )}
    </div>
  )
}

type InputProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'onChange' | 'value' | 'id'>

interface TextFieldProps extends InputProps {
  label: ReactNode
  value: string
  onChange: (value: string) => void
  hint?: ReactNode
  error?: string
  fieldClassName?: string
}

export function TextField({ label, value, onChange, hint, error, fieldClassName, required, ...rest }: TextFieldProps) {
  const id = useId()
  return (
    <FieldShell id={id} label={label} hint={hint} error={error} className={fieldClassName}>
      <input
        id={id}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        aria-invalid={error ? true : undefined}
        aria-required={required || undefined}
        aria-describedby={describedBy(id, hint, error)}
        {...rest}
      />
    </FieldShell>
  )
}

interface TextAreaFieldProps extends Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, 'onChange' | 'value' | 'id'> {
  label: ReactNode
  value: string
  onChange: (value: string) => void
  hint?: ReactNode
  error?: string
}

export function TextAreaField({ label, value, onChange, hint, error, required, ...rest }: TextAreaFieldProps) {
  const id = useId()
  return (
    <FieldShell id={id} label={label} hint={hint} error={error}>
      <textarea
        id={id}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        aria-invalid={error ? true : undefined}
        aria-required={required || undefined}
        aria-describedby={describedBy(id, hint, error)}
        {...rest}
      />
    </FieldShell>
  )
}

export interface Option {
  value: string
  label: string
}

interface SelectFieldProps extends Omit<SelectHTMLAttributes<HTMLSelectElement>, 'onChange' | 'value' | 'id'> {
  label: ReactNode
  value: string
  onChange: (value: string) => void
  options: Option[]
  hint?: ReactNode
  error?: string
}

export function SelectField({ label, value, onChange, options, hint, error, ...rest }: SelectFieldProps) {
  const id = useId()
  return (
    <FieldShell id={id} label={label} hint={hint} error={error}>
      <select
        id={id}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy(id, hint, error)}
        {...rest}
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </FieldShell>
  )
}

interface CheckboxFieldProps extends Omit<InputProps, 'type' | 'checked'> {
  label: ReactNode
  checked: boolean
  onCheckedChange: (checked: boolean) => void
  hint?: ReactNode
}

export function CheckboxField({ label, checked, onCheckedChange, hint, ...rest }: CheckboxFieldProps) {
  const id = useId()
  return (
    <div className="field field-check">
      <input
        id={id}
        type="checkbox"
        checked={checked}
        onChange={(event) => onCheckedChange(event.target.checked)}
        aria-describedby={hint ? `${id}-hint` : undefined}
        {...rest}
      />
      <div>
        <label htmlFor={id}>{label}</label>
        {hint && (
          <p className="field-hint" id={`${id}-hint`}>
            {hint}
          </p>
        )}
      </div>
    </div>
  )
}

interface ReadOnlyFieldProps {
  label: ReactNode
  value: string
  mono?: boolean
  action?: ReactNode
}

/** A labelled value that can be selected and copied but not edited. */
export function ReadOnlyField({ label, value, mono = false, action }: ReadOnlyFieldProps) {
  const id = useId()
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <div className="field-inline">
        <input id={id} value={value} readOnly className={mono ? 'mono' : undefined} />
        {action}
      </div>
    </div>
  )
}
