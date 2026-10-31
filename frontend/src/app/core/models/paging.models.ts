/** Server-side page envelope shared by every paginated list endpoint. */
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}
