import type { de } from './de';

export const en: typeof de = {
	memberTitle: 'First steps',
	adminTitle: 'Set up your organization',
	progress: '{done} of {total} done',
	dismiss: 'Hide checklist',
	done: 'done',
	steps: {
		schedule: { label: 'Set up your work schedule', hint: 'Your target hours – without them there is no balance.' },
		timer: { label: 'Start the timer', hint: 'One click when you start work, one when you stop.' },
		teamCalendar: { label: 'Look at the team calendar', hint: 'Who is away this week?' },
		invite: { label: 'Invite members', hint: 'By email – they join with one click.' },
		rules: { label: 'Check rules & visibility', hint: 'What members may do themselves and see about others.' },
		holidays: { label: 'Import holidays', hint: 'For your region, so holidays never count as missing days.' }
	}
};
