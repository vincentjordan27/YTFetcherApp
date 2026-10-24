const compactCountFormatter = new Intl.NumberFormat('en-US', {
  notation: 'compact',
  maximumFractionDigits: 1,
})

/** Formats one real count for the compact shell badge; number formatting never lives in an API model. */
export function formatCompactCount(value: number): string {
  return compactCountFormatter.format(value)
}
