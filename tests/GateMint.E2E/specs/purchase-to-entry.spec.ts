import { expect, test, type Page } from '@playwright/test'

const password = process.env.GATEMINT_DEMO_PASSWORD
async function signIn(page: Page, email: string) {
  await page.goto('/auth')
  await page.getByLabel('Email address').fill(email)
  await page.getByLabel('Password').fill(password!)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page).toHaveURL(/\/$/)
}

test.skip(!password, 'GATEMINT_DEMO_PASSWORD is required')

test('organizer publishing and attendee purchase reach a single gate admission', async ({ page }) => {
  await signIn(page, 'organizer@gatemint.local')
  await page.getByRole('link', { name: 'Organize' }).click()
  await expect(page.getByRole('heading', { name: 'Build a full house.' })).toBeVisible()
  const eventTitle = `Design After Dark ${Date.now()}`
  await page.getByLabel('Event title').fill(eventTitle)
  await page.getByLabel('Description').fill('A new independent design gathering.')
  await page.getByLabel('Location').fill('Lagos Island')
  const tomorrow = new Date(Date.now() + 86_400_000)
  const nextDay = new Date(Date.now() + 172_800_000)
  await page.getByLabel('Starts').fill(tomorrow.toISOString().slice(0, 16))
  await page.getByLabel('Ends').fill(nextDay.toISOString().slice(0, 16))
  await page.getByRole('button', { name: 'Save draft' }).click()
  const eventRow = page.getByRole('button').filter({ hasText: eventTitle })
  await expect(eventRow).toBeVisible()
  await eventRow.getByText('Add tier & publish').click()
  await expect(eventRow).toContainText('Published')
  await page.getByRole('button', { name: 'Sign out' }).click()

  await signIn(page, 'attendee@gatemint.local')
  await page.getByText('Lagos Night Market').first().click()
  const reservationResponse = page.waitForResponse(response =>
    response.url().endsWith('/api/v1/orders') && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Reserve for 10 minutes' }).click()
  const reservation = await (await reservationResponse).json()
  await expect(page.getByRole('heading', { name: 'Complete test payment' })).toBeVisible()
  await page.getByRole('button', { name: 'Approve simulated payment' }).click()
  await expect(page).toHaveURL(/\/orders/)
  await expect(page.getByRole('link', { name: 'Ticket 1' }).first()).toBeVisible()

  const confirmed = await page.request.get(`/api/v1/orders/${reservation.orderId}`).then(response => response.json())
  expect(confirmed.status).toBe('Confirmed')
  const pass = await page.request.get(`/api/v1/tickets/${confirmed.ticketIds[0]}`).then(response => response.json())
  const events = await page.request.get('/api/v1/events?search=Lagos%20Night%20Market').then(response => response.json())
  await page.getByRole('button', { name: 'Sign out' }).click()

  await signIn(page, 'staff@gatemint.local')
  await page.getByRole('link', { name: 'Scan' }).click()
  await page.getByLabel('Event ID').fill(events.items[0].id)
  await page.getByLabel('Manual ticket code').fill(pass.qrData)
  await page.getByRole('button', { name: 'Validate ticket' }).click()
  await expect(page.getByText('Entry approved.')).toBeVisible()
  await page.getByRole('button', { name: 'Validate ticket' }).click()
  await expect(page.getByText('This ticket was already admitted.')).toBeVisible()
})
