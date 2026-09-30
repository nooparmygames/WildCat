import { StreamLanguage } from '@codemirror/language'
import { csharp } from '@codemirror/legacy-modes/mode/clike'
import CodeMirror, { EditorView } from '@uiw/react-codemirror'
import { useId, useMemo } from 'react'
import { t } from '../../i18n'
import { useColorScheme } from '../../lib/useColorScheme'

const csharpLanguage = StreamLanguage.define(csharp)

interface ScriptEditorProps {
  label: string
  value: string
  onChange: (value: string) => void
  hint?: string
  invalid?: boolean
}

/** A C# code editor (CodeMirror 6 with the legacy clike C# mode). */
export function ScriptEditor({ label, value, onChange, hint, invalid }: ScriptEditorProps) {
  const labelId = useId()
  const hintId = useId()
  const scheme = useColorScheme()

  const extensions = useMemo(
    () => [
      csharpLanguage,
      EditorView.lineWrapping,
      EditorView.contentAttributes.of({
        'aria-labelledby': labelId,
        'aria-describedby': hintId,
        ...(invalid ? { 'aria-invalid': 'true' } : {}),
      }),
    ],
    [labelId, hintId, invalid],
  )

  return (
    <div className={`field${invalid ? ' field-invalid' : ''}`}>
      <span className="field-label" id={labelId}>
        {label}
      </span>
      <div className="code-editor">
        <CodeMirror
          value={value}
          onChange={onChange}
          extensions={extensions}
          theme={scheme}
          minHeight="280px"
          maxHeight="70vh"
          basicSetup={{ foldGutter: false }}
        />
      </div>
      <p className="field-hint" id={hintId}>
        {hint ? `${hint} ` : ''}
        {t('events.editorKeyboardHint')}
      </p>
    </div>
  )
}
