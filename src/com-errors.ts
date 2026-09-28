/**
 * COM error metadata for throwing native errors in the classic IE/WSH shape.
 *
 * When a COM method fails, the bridge recovers the HRESULT from the exception chain
 * (see Reflection.FindComHResult). A FACILITY_VBS HRESULT (0x800Axxxx) encodes a
 * VBScript error code in its low 16 bits; mapping that back to a human description is
 * what lets a plain JS host catch a native Error whose `.message`/`.description` read
 * like the one JScript receives from Internet Explorer / Windows Script Host, without
 * any dependence on a VBScript engine.
 */

/**
 * English descriptions for the standard VBScript runtime error codes, keyed by code.
 * Mirrors the canonical VBScript "Err.Description" strings; the real host localizes
 * these, but this bridge's own strings are English throughout.
 */
const VBS_CODE_DESCRIPTIONS: Record<number, string> = {
  5: 'Invalid procedure call or argument',
  6: 'Overflow',
  7: 'Out of memory',
  9: 'Subscript out of range',
  10: 'This array is fixed or temporarily locked',
  11: 'Division by zero',
  13: 'Type mismatch',
  14: 'Out of string space',
  17: "Can't perform requested operation",
  28: 'Out of stack space',
  35: 'Sub or Function not defined',
  48: 'Error in loading DLL',
  51: 'Internal error',
  52: 'Bad file name or number',
  53: 'File not found',
  54: 'Bad file mode',
  55: 'File already open',
  57: 'Device I/O error',
  58: 'File already exists',
  61: 'Disk full',
  62: 'Input past end of file',
  67: 'Too many files',
  68: 'Device unavailable',
  70: 'Permission denied',
  71: 'Disk not ready',
  74: "Can't rename with different drive",
  75: 'Path/File access error',
  76: 'Path not found',
  91: 'Object variable or With block variable not set',
  92: 'For loop not initialized',
  94: 'Invalid use of Null',
  322: "Can't create necessary temporary file",
  424: 'Object required',
  429: "ActiveX component can't create object",
  430: "Class doesn't support Automation",
  432: 'File name or class name not found during Automation operation',
  438: "Object doesn't support this property or method",
  440: 'Automation error',
  442:
    'Connection to type library or object library for remote process has been lost. ' +
    'Press OK for dialog box to remove reference.',
  443: "Automation object doesn't have a default value",
  445: "Object doesn't support this action",
  446: "Object doesn't support named arguments",
  447: "Object doesn't support current locale setting",
  448: 'Named argument not found',
  449: 'Argument not optional',
  450: 'Wrong number of arguments or invalid property assignment',
  451: 'Object not a collection',
  453: 'Specified DLL function not found',
  455: 'Code resource lock error',
  457: 'This key is already associated with an element of this collection',
  458: 'Variable uses an Automation type not supported in VBScript',
  462: 'The remote server machine does not exist or is unavailable',
  481: 'Invalid picture',
  500: 'Variable is undefined',
  501: 'Illegal assignment',
  502: 'Object not safe for scripting',
  503: 'Object not safe for initializing',
  504: 'Object not safe for creating',
  505: 'Invalid or unqualified reference',
};

/** FACILITY_VBS (0x800A) — the facility whose HRESULTs carry a VBScript error code. */
const FACILITY_VBS = 0x800a;

/**
 * Map a COM HRESULT to its canonical English description, or null when the HRESULT is
 * not a FACILITY_VBS error with a known code. Non-VBS facilities (e.g. 0x8004xxxx
 * FACILITY_ITF, 0x800401F3 CO_E_CLASSSTRING) have component-specific messages that the
 * bridge cannot localize, so those fall back to the raw .NET message.
 */
export function hresultToDescription(hresult: number): string | null {
  if (hresult >>> 16 !== FACILITY_VBS) return null;
  return VBS_CODE_DESCRIPTIONS[hresult & 0xffff] ?? null;
}
