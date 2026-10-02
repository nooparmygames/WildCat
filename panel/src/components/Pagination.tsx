import { t } from '../i18n'

interface PaginationProps {
  page: number
  pageSize: number
  total: number
  onPageChange: (page: number) => void
}

export function pageCount(total: number, pageSize: number): number {
  return Math.max(1, Math.ceil(total / pageSize))
}

export function Pagination({ page, pageSize, total, onPageChange }: PaginationProps) {
  const pages = pageCount(total, pageSize)
  return (
    <nav className="pagination" aria-label={t('common.pagination')}>
      <button type="button" className="btn btn-small" onClick={() => onPageChange(page - 1)} disabled={page <= 1}>
        {t('common.previous')}
      </button>
      <span>{t('common.pageOf', { page, pages })}</span>
      <button type="button" className="btn btn-small" onClick={() => onPageChange(page + 1)} disabled={page >= pages}>
        {t('common.next')}
      </button>
    </nav>
  )
}
