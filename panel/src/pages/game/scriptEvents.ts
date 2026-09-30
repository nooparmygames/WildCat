import { parameterCSharpTypes, parameterTypes, type EventParameter } from '../../api/types'

export function csharpType(type: number): string {
  const name = parameterTypes[type]
  return name ? parameterCSharpTypes[name] : 'object'
}

/** The parameter list as C# would declare it, e.g. "int level, string mode". */
export function parameterList(parameters: EventParameter[]): string {
  return parameters.map((p) => `${csharpType(p.type)} ${p.name || '_'}`).join(', ')
}

/**
 * The method the script has to define. The server calls a method named after the event with the
 * parameters in order; returning Task<T> instead of Task sends a value back to the caller.
 */
export function methodSignature(name: string, parameters: EventParameter[]): string {
  return `async Task ${name || 'EventName'}(${parameterList(parameters)})`
}

export function methodTemplate(name: string, parameters: EventParameter[]): string {
  return `${methodSignature(name, parameters)}\n{\n    await Task.CompletedTask;\n}\n`
}
