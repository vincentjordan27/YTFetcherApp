const DEFAULT_PAGE_SIZE = 10
const MINIMUM_PAGE_SIZE = 5
const MAXIMUM_PAGE_SIZE = 100
const TABLE_CHROME_HEIGHT_PX = 240
const ESTIMATED_TABLE_ROW_HEIGHT_PX = 70

export function getInitialTablePageSize(): number {
  if (typeof window === 'undefined') return DEFAULT_PAGE_SIZE

  const availableRowHeight = window.innerHeight - TABLE_CHROME_HEIGHT_PX
  const estimatedRowCount = Math.floor(availableRowHeight / ESTIMATED_TABLE_ROW_HEIGHT_PX)

  return Math.min(MAXIMUM_PAGE_SIZE, Math.max(MINIMUM_PAGE_SIZE, estimatedRowCount))
}
