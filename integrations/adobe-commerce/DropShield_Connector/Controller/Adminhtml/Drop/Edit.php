<?php

declare(strict_types=1);

namespace DropShield\Connector\Controller\Adminhtml\Drop;

use Magento\Backend\App\Action;
use Magento\Framework\Controller\ResultFactory;

class Edit extends Action
{
    public const ADMIN_RESOURCE = 'DropShield_Connector::protected_drops';

    public function execute()
    {
        $page = $this->resultFactory->create(ResultFactory::TYPE_PAGE);
        $page->setActiveMenu(self::ADMIN_RESOURCE);
        $page->getConfig()->getTitle()->prepend($this->getRequest()->getParam('id') ? __('Edit Protected Drop') : __('Create Protected Drop'));
        return $page;
    }
}
