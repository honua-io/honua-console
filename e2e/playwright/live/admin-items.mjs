export async function readAdminItems(getJson, path) {
  const url = new URL(path, 'http://admin.invalid');
  const items = [];
  for (let page = 1; ; page++) {
    url.searchParams.set('page', String(page));
    const { data } = await getJson(url.pathname + url.search);
    if (!data || !Array.isArray(data.items) || data.page !== page
      || !Number.isSafeInteger(data.pageSize) || data.pageSize <= 0
      || !Number.isSafeInteger(data.total) || data.total < 0
      || data.items.length > data.pageSize
      || (data.items.length === 0 && items.length < data.total)) {
      throw new Error(`Invalid admin page for ${url.pathname}`);
    }
    items.push(...data.items);
    if (page * data.pageSize >= data.total) return items;
  }
}
